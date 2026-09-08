using Microsoft.UI.Xaml;
using PCActivityLog.Data;
using PCActivityLog.Services;
using PCActivityLog.Watchers;

namespace PCActivityLog;

/// <summary>
/// 应用入口（WinUI 3）—— 组装根。
/// 职责：单实例控制、全局异常兜底、构建并启动服务与监视模块、优雅退出。
/// 与 WPF 版的差异：生命周期回调换成 WinUI 的 Application；托盘由 MainWindow 挂 H.NotifyIcon。
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
    private Mutex? _mutex;
    private EventWaitHandle? _activateSignal;
    private Thread? _activateListener;
    private volatile bool _shuttingDown;

    private RetentionService? _retention;
    private ImFileStatusService? _imStatus;
    private NotificationService? _notifier;

    /// <summary>启动时是否带 --minimized（开机自启静默驻留托盘）。</summary>
    public static bool StartMinimized { get; private set; }

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
        // 1. 单实例：已有实例则发激活信号后退出
        _mutex = new Mutex(true, MutexName, out var createdNew);
        if (!createdNew)
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
        _activateSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
        _activateListener = new Thread(ActivateListenLoop) { IsBackground = true, Name = "ActivateListener" };
        _activateListener.Start();

        // 2. 解析启动参数
        var cmdArgs = Environment.GetCommandLineArgs();
        StartMinimized = cmdArgs.Any(a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase));

        // 3. 组装服务与监视模块
        DiagnosticsLog.Info("========== 程序启动（WinUI 3） ==========");
        Settings = AppSettings.Load();
        AutoStartService.RefreshPathIfEnabled();

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

        // 4. 主窗口（含托盘）
        MainWindowInstance = new MainWindow(_notifier);
        MainWindowInstance.Activate();

        DiagnosticsLog.Info("程序启动完成");
    }

    // ---------- 激活监听（第二实例唤醒） ----------

    private void ActivateListenLoop()
    {
        try
        {
            while (!_shuttingDown)
            {
                if (_activateSignal!.WaitOne(500))
                    MainWindowInstance?.DispatcherQueue.TryEnqueue(() => MainWindowInstance.ShowFromTray());
            }
        }
        catch (ObjectDisposedException) { }
    }

    // ---------- 退出 ----------

    /// <summary>
    /// 真正退出（托盘菜单「退出」调用）。
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
            SafeRun("释放激活信号", () => _activateSignal?.Dispose());
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
}
