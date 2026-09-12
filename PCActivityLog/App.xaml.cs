using Microsoft.UI.Xaml;
using PCActivityLog.Data;
using PCActivityLog.Services;
using PCActivityLog.Watchers;

using System.Runtime.InteropServices;

namespace PCActivityLog;

/// <summary>
/// 应用入口（WinUI 3）—— 组装根。
/// 职责：单实例控制、全局异常兜底、自我登记、构建并启动服务与监视模块、
/// 卸载流程（--uninstall）与优雅退出。
/// </summary>
public partial class App : Application
{
    // ---------- 静态访问点 ----------
    public static AppSettings? Settings { get; private set; }
    public static Database? Db { get; private set; }
    public static WriteQueue? WriteQueueInstance { get; private set; }
    public static WatcherManager? Manager { get; private set; }

    /// <summary>时间线页的 ViewModel 引用（页面被 Frame 缓存复用，退出时统一释放其事件订阅）。</summary>
    public static ViewModels.TimelineViewModel? TimelinePageVm { get; set; }
    public static MainWindow? MainWindowInstance { get; private set; }

    /// <summary>允许真正退出（点关闭按钮时若设置最小化到托盘，则仅隐藏窗口）。</summary>
    public static bool AllowClose { get; set; }

    // ---------- 单实例 ----------
    private const string MutexName = @"Local\PCActivityLog_SingleInstance";
    private const string ActivateEventName = @"Local\PCActivityLog_Activate";
    private const string ExitEventName = @"Local\PCActivityLog_Exit";
    private Mutex? _mutex;
    private EventWaitHandle? _activateSignal;
    private EventWaitHandle? _exitSignal;
    private Thread? _activateListener;
    private volatile bool _shuttingDown;

    private RetentionService? _retention;
    private ImFileStatusService? _imStatus;
    private NotificationService? _notifier;

    /// <summary>启动时是否带 --minimized（开机自启静默驻留托盘）。</summary>
    public static bool StartMinimized { get; private set; }

    /// <summary>启动时是否带 --uninstall（卸载流程：清除系统登记与自启动，不启动主功能）。</summary>
    public static bool UninstallMode { get; private set; }

    public App()
    {
        InitializeComponent();
        // 全局异常兜底
        UnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            DiagnosticsLog.Error("未处理异常（AppDomain）", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            DiagnosticsLog.Error("未观察的 Task 异常", e.Exception);
            e.SetObserved();
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // 1. 解析启动参数
        var cmdArgs = Environment.GetCommandLineArgs();
        StartMinimized = cmdArgs.Any(a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase));
        UninstallMode = cmdArgs.Any(a => a.Equals("--uninstall", StringComparison.OrdinalIgnoreCase));

        // 2. 单实例：已有实例时，普通模式唤醒它后退出；卸载模式请求它退出并接管
        _mutex = new Mutex(true, MutexName, out var createdNew);
        if (!createdNew)
        {
            if (UninstallMode)
            {
                // 请求运行中的实例退出（其监听循环收到后走统一退出流程）
                try
                {
                    using var exitEvt = EventWaitHandle.OpenExisting(ExitEventName);
                    exitEvt.Set();
                }
                catch { }

                // 最多等 5 秒拿到互斥体（原实例正常释放或异常退出都算已让位）
                var acquired = false;
                try
                {
                    acquired = _mutex.WaitOne(TimeSpan.FromSeconds(5));
                }
                catch (AbandonedMutexException)
                {
                    acquired = true;
                }
                if (!acquired)
                {
                    MessageBoxW(IntPtr.Zero,
                        "请先退出正在运行的电脑日志记录（托盘右键 → 退出），再执行卸载。",
                        "卸载 电脑日志记录", 0x40 /* MB_ICONINFORMATION */);
                    Exit();
                    return;
                }
                // 已接管互斥体，继续以"卸载实例"身份运行
            }
            else
            {
                try
                {
                    using var evt = EventWaitHandle.OpenExisting(ActivateEventName);
                    evt.Set();
                }
                catch { }
                Exit();
                return;
            }
        }
        _activateSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
        _exitSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ExitEventName);
        _activateListener = new Thread(ActivateListenLoop) { IsBackground = true, Name = "ActivateListener" };
        _activateListener.Start();

        // 3. 组装服务
        DiagnosticsLog.Info("========== 程序启动（WinUI 3） ==========");
        Settings = AppSettings.Load();
        AutoStartService.RefreshPathIfEnabled();

        // 自我登记/清除 —— 必须在监视模块启动前完成，
        // 这样注册表监视的基线快照天然包含自己的键（另加键名排除，双保险）
        if (!UninstallMode)
        {
            if (Settings.RegisterInSystem) AppRegistrationService.Register();
            else AppRegistrationService.Unregister();
        }

        // 4. 卸载模式：不启动数据库/监视/主窗口，只跑卸载确认窗口
        if (UninstallMode)
        {
            var win = new Views.UninstallWindow();
            win.Activate(); // 关闭时经 Closed 事件走 ExitApplication
            return;
        }

        // 5. 数据与服务
        Db = new Database();
        Db.Initialize();

        WriteQueueInstance = new WriteQueue(Db);
        _notifier = new NotificationService(Settings);
        var linker = new EventLinker(Db);

        Manager = new WatcherManager(Settings, Db, WriteQueueInstance, _notifier, linker);
        Manager.BuildModules();
        Manager.StartAll();

        _retention = new RetentionService(Settings, Db);
        _retention.Start();

        _imStatus = new ImFileStatusService(Db);
        _imStatus.Start();

        // 6. 主窗口（含托盘）
        MainWindowInstance = new MainWindow(_notifier);
        // Activate 是必需的（WinUI 窗口首次激活后才完成初始化，托盘/页面才可用）
        MainWindowInstance.Activate();
        // 自启动（--minimized）：激活完成后立即隐藏到托盘。
        // 注意必须在 Activate 之后隐藏——若在构造函数里隐藏会被这里的 Activate 重新显示。
        if (StartMinimized) MainWindowInstance.HideToTray();

        DiagnosticsLog.Info("程序启动完成");
    }

    // ---------- 激活/退出监听（第二实例唤醒或请求退出） ----------

    private void ActivateListenLoop()
    {
        try
        {
            var handles = new WaitHandle[] { _activateSignal!, _exitSignal! };
            while (!_shuttingDown)
            {
                var idx = WaitHandle.WaitAny(handles, 500);
                if (idx == 0)
                    MainWindowInstance?.DispatcherQueue.TryEnqueue(() => MainWindowInstance.ShowFromTray());
                else if (idx == 1)
                {
                    // 卸载实例请求本实例退出（只处理一次，然后停止监听）
                    MainWindowInstance?.DispatcherQueue.TryEnqueue(() => ExitApplication());
                    break;
                }
            }
        }
        catch (ObjectDisposedException) { }
    }

    // ---------- 退出 ----------

    /// <summary>
    /// 真正退出（托盘菜单「退出」/卸载流程调用）。
    /// 关键：先隐藏窗口并让用户立即看到反馈，再把清理工作放到后台线程执行，
    /// 避免模块停止/队列冲写阻塞 UI 线程导致"点了没反应"的错觉。
    /// </summary>
    public void ExitApplication()
    {
        if (_shuttingDown) return; // 防止重复触发
        AllowClose = true;
        _shuttingDown = true;

        DiagnosticsLog.Info("开始退出流程…");

        // 1. 先隐藏窗口和托盘图标（立即反馈）
        SafeRun("隐藏窗口", () => MainWindowInstance?.HideForExit());
        SafeRun("移除托盘图标", () => MainWindowInstance?.DisposeTray());

        // 2. 清理放后台执行，避免阻塞 UI 线程
        Task.Run(() =>
        {
            SafeRun("停止监视模块", () => Manager?.Dispose());
            SafeRun("停止数据清理", () => _retention?.Dispose());
            SafeRun("停止 IM 状态复查", () => _imStatus?.Dispose());
            SafeRun("释放页面 ViewModel", () => TimelinePageVm?.Dispose());
            SafeRun("冲写数据库队列", () => WriteQueueInstance?.Dispose());
            SafeRun("释放激活/退出信号", () =>
            {
                _activateSignal?.Dispose();
                _exitSignal?.Dispose();
            });
            SafeRun("释放单实例互斥体", () =>
            {
                if (_mutex != null && !_mutex.SafeWaitHandle.IsClosed)
                {
                    try { _mutex.ReleaseMutex(); } catch (ApplicationException) { }
                }
                _mutex?.Dispose();
            });
            SafeRun("清空数据库连接池", () => Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools());
            DiagnosticsLog.Info("========== 程序退出 ==========");
        }).Wait(TimeSpan.FromSeconds(5)); // 最多等 5 秒，超时也强制退出

        // 3. 结束进程
        Environment.Exit(0);
    }

    private static void SafeRun(string step, Action action)
    {
        try { action(); }
        catch (Exception ex) { DiagnosticsLog.Error($"退出步骤失败：{step}", ex); }
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        DiagnosticsLog.Error("UI 线程未处理异常: " + e.Exception);
        e.Handled = true;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
}
