using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Hardcodet.Wpf.TaskbarNotification;
using PCActivityLog.Data;
using PCActivityLog.Services;
using PCActivityLog.Ui;
using PCActivityLog.ViewModels;
using PCActivityLog.Views;
using PCActivityLog.Watchers;

namespace PCActivityLog;

/// <summary>
/// 应用程序入口 —— 组装根（Composition Root）。
/// 职责：单实例控制、全局异常兜底、构建并启动全部服务与模块、托盘图标、优雅关闭。
/// 生命周期链：OnStartup 创建一切 → 主循环 → 退出（托盘菜单「退出」）→ OnExit 按序收尾：
///   停止模块 → 冲写数据库队列 → 停清理服务 → 释放托盘 → 释放互斥体 → 清连接池。
/// </summary>
public partial class App : Application
{
    // ---------- 静态访问点（供窗口/VM 使用，避免层层传参） ----------
    public static AppSettings? Settings { get; private set; }
    public static Database? Db { get; private set; }
    public static WriteQueue? WriteQueueInstance { get; private set; }

    /// <summary>允许真正关闭（点 X 时若设置了最小化到托盘，则只是隐藏）。</summary>
    public static bool AllowClose { get; set; }

    // ---------- 单实例 ----------
    private const string MutexName = @"Local\PCActivityLog_SingleInstance";
    private const string ActivateEventName = @"Local\PCActivityLog_Activate";
    private Mutex? _mutex;
    private EventWaitHandle? _activateSignal;
    private Thread? _activateListener;
    private volatile bool _shuttingDown;

    // ---------- 核心组件 ----------
    private WatcherManager? _manager;
    private RetentionService? _retention;
    private ImFileStatusService? _imStatus;
    private NotificationService? _notifier;
    private TaskbarIcon? _tray;
    private MainWindow? _mainWindow;
    private SettingsWindow? _settingsWindow;

    // ---------- 启动 ----------

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 1. 全局异常兜底（规范第 5 条）——尽早挂上
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        // 2. 单实例：已有实例则发激活信号后退出
        _mutex = new Mutex(true, MutexName, out var createdNew);
        if (!createdNew)
        {
            try
            {
                using var evt = EventWaitHandle.OpenExisting(ActivateEventName);
                evt.Set(); // 让已运行的实例把主窗口弹出来
            }
            catch { /* 对方可能正好在退出 */ }
            Shutdown();
            return;
        }
        _activateSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
        _activateListener = new Thread(ActivateListenLoop) { IsBackground = true, Name = "ActivateListener" };
        _activateListener.Start();

        // 3. 组装核心组件
        DiagnosticsLog.Info("========== 程序启动 ==========");
        Settings = AppSettings.Load();
        AutoStartService.RefreshPathIfEnabled();

        // 主题在创建任何窗口之前应用，首帧即正确配色
        ThemeService.Apply(Settings);

        Db = new Database();
        Db.Initialize();

        WriteQueueInstance = new WriteQueue(Db);
        _notifier = new NotificationService(Settings, () => _tray);
        var linker = new EventLinker(Db);

        _manager = new WatcherManager(Settings, Db, WriteQueueInstance, _notifier, linker);
        _manager.BuildModules();
        _manager.StartAll();

        _retention = new RetentionService(Settings, Db);
        _retention.Start();

        _imStatus = new ImFileStatusService(Db);
        _imStatus.Start();

        // 4. 主窗口与托盘
        var exporter = new ExportService(Db);
        var mainVm = new MainViewModel(Db, WriteQueueInstance, exporter);
        var statsVm = new StatsViewModel(new StatsRepository(Db));
        mainVm.Refresh();

        _mainWindow = new MainWindow(mainVm) { Icon = TrayIconFactory.CreateWindowIcon() };
        MainWindow = _mainWindow; // 设为 Application.MainWindow，供自动刷新判断可见性
        _mainWindow.Loaded += (_, _) => mainVm.Refresh();
        mainVm.Stats = statsVm;

        // 系统主题切换时重建行项目（徽章颜色按主题缓存）并刷新统计
        ThemeService.ThemeChanged += () =>
        {
            Dispatcher.BeginInvoke(() =>
            {
                mainVm.Refresh();
                statsVm.Refresh();
            });
        };

        CreateTray();

        // 5. 显示窗口：开机自启动（--minimized 参数）时静默驻留托盘
        var startMinimized = e.Args.Any(a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase));
        if (!startMinimized)
            _mainWindow.Show();

        DiagnosticsLog.Info("程序启动完成");
    }

    // ---------- 托盘 ----------

    /// <summary>创建托盘图标与右键菜单。</summary>
    private void CreateTray()
    {
        _tray = new TaskbarIcon
        {
            IconSource = TrayIconFactory.Create(),
            ToolTipText = "电脑日志记录 — 双击打开",
        };
        _tray.TrayMouseDoubleClick += (_, _) => ShowMainWindow();

        var menu = new ContextMenu();

        var openItem = new MenuItem { Header = "显示主窗口" };
        openItem.Click += (_, _) => ShowMainWindow();
        menu.Items.Add(openItem);

        var settingsItem = new MenuItem { Header = "设置" };
        settingsItem.Click += (_, _) => ShowSettingsWindow();
        menu.Items.Add(settingsItem);

        var autoStartItem = new MenuItem
        {
            Header = "开机自启动",
            IsCheckable = true,
            IsChecked = AutoStartService.IsEnabled(),
        };
        autoStartItem.Click += (_, _) =>
        {
            var enable = autoStartItem.IsChecked;
            AutoStartService.SetEnabled(enable);
            if (Settings != null) { Settings.StartWithWindows = enable; Settings.Save(); }
        };
        menu.Items.Add(autoStartItem);

        menu.Items.Add(new Separator());

        var exitItem = new MenuItem { Header = "退出" };
        exitItem.Click += (_, _) => ExitApplication();
        menu.Items.Add(exitItem);

        _tray.ContextMenu = menu;
    }

    /// <summary>显示（并激活）主窗口。</summary>
    public static void ShowMainWindow()
    {
        if (Current is not App app || app._mainWindow == null) return;
        app._mainWindow.Dispatcher.BeginInvoke(() =>
        {
            app._mainWindow.Show();
            if (app._mainWindow.WindowState == WindowState.Minimized)
                app._mainWindow.WindowState = WindowState.Normal;
            app._mainWindow.Activate();
        });
    }

    /// <summary>打开设置窗口（已打开则激活，不重复开）。</summary>
    public static void ShowSettingsWindow()
    {
        if (Current is not App app || app._manager == null || Settings == null) return;
        app.Dispatcher.BeginInvoke(() =>
        {
            if (app._settingsWindow != null)
            {
                app._settingsWindow.Activate();
                return;
            }
            var vm = new SettingsViewModel(Settings, app._manager);
            app._settingsWindow = new SettingsWindow(vm) { Owner = app._mainWindow };
            // 窗口关闭即清空引用，避免滞留已关闭的窗口对象
            app._settingsWindow.Closed += (_, _) => app._settingsWindow = null;
            app._settingsWindow.Show();
        });
    }

    /// <summary>「添加记录」命令的桥接：弹对话框并入队。</summary>
    public static void ShowAddEventDialog(Window owner)
    {
        var dlg = new AddEventDialog { Owner = owner };
        if (dlg.ShowDialog() == true && dlg.Result != null)
            WriteQueueInstance?.Enqueue(dlg.Result);
    }

    /// <summary>首次隐藏到托盘时给个气泡提示，避免用户以为程序退了。</summary>
    private bool _trayTipShown;

    public static void OnWindowHiddenToTray()
    {
        if (Current is not App app || app._trayTipShown) return;
        app._trayTipShown = true;
        app._tray?.ShowBalloonTip("电脑日志记录", "程序已最小化到托盘，继续后台记录。双击托盘图标可重新打开。",
            BalloonIcon.Info);
    }

    // ---------- 激活监听（第二实例唤醒） ----------

    /// <summary>后台线程：等待激活信号 → 弹出主窗口。500ms 轮询退出标志，关机不卡线程。</summary>
    private void ActivateListenLoop()
    {
        try
        {
            while (!_shuttingDown)
            {
                if (_activateSignal!.WaitOne(500))
                    ShowMainWindow();
            }
        }
        catch (ObjectDisposedException) { /* 退出时句柄已释放，正常 */ }
    }

    // ---------- 退出 ----------

    /// <summary>真正的退出流程（托盘菜单「退出」调用）。</summary>
    public void ExitApplication()
    {
        AllowClose = true;
        _shuttingDown = true;
        _mainWindow?.Close(); // 触发 OnClosing（AllowClose=true 时不再拦截）
        Shutdown();           // → OnExit 收尾
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // 优雅关闭（规范第 7 条）：按依赖顺序收尾，每步独立 try/catch
        SafeRun("停止监视模块", () => _manager?.Dispose());
        SafeRun("停止数据清理", () => _retention?.Dispose());
        SafeRun("停止 IM 状态复查", () => _imStatus?.Dispose());
        SafeRun("冲写数据库队列", () => WriteQueueInstance?.Dispose()); // 内部限时 3 秒
        SafeRun("释放托盘图标", () => _tray?.Dispose());
        SafeRun("释放激活信号", () =>
        {
            _shuttingDown = true;
            _activateSignal?.Dispose();
        });
        SafeRun("释放单实例互斥体", () =>
        {
            if (_mutex != null && _mutex.SafeWaitHandle.IsClosed == false)
            {
                try { _mutex.ReleaseMutex(); } catch (ApplicationException) { /* 非持有线程调用时忽略 */ }
            }
            _mutex?.Dispose();
        });
        SafeRun("清空数据库连接池", () => Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools());

        DiagnosticsLog.Info("========== 程序退出 ==========");
        base.OnExit(e);
    }

    private static void SafeRun(string step, Action action)
    {
        try { action(); }
        catch (Exception ex) { DiagnosticsLog.Error($"退出步骤失败：{step}", ex); }
    }

    // ---------- 全局异常兜底 ----------

    /// <summary>UI 线程异常：记日志并吞掉（程序继续运行）。</summary>
    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // ex.ToString() 自带完整 InnerException 链，排查模板/XAML 问题必需
        DiagnosticsLog.Error("UI 线程未处理异常: " + e.Exception);
        e.Handled = true;
    }

    /// <summary>后台线程致命异常：只能记日志（进程可能继续）。</summary>
    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        => DiagnosticsLog.Error("未处理异常（AppDomain）", e.ExceptionObject as Exception);

    /// <summary>未观察的 Task 异常：记日志并标记已观察，防进程崩溃。</summary>
    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        DiagnosticsLog.Error("未观察的 Task 异常", e.Exception);
        e.SetObserved();
    }
}
