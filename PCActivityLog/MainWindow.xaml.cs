using Microsoft.UI.Xaml;
using PCActivityLog.Services;
using PCActivityLog.Views;

using System.Runtime.InteropServices;

namespace PCActivityLog;

/// <summary>
/// 主窗口 —— 时间线铺满窗口（无导航栏），设置从时间线工具栏齿轮进入。
/// 托盘为 Windows 原生 Shell_NotifyIcon（TrayIconService），托盘菜单为 Win32 原生实现。
/// </summary>
public sealed partial class MainWindow : Window
{
    private readonly NotificationService _notifier;
    private TrayIconService? _tray;

    public MainWindow(NotificationService notifier)
    {
        _notifier = notifier;
        InitializeComponent();

        TryEnableMica();
        ApplyWindowIcon(); // 窗口/任务栏图标（WinUI3 需显式设置）
        ThemeService.Apply(App.Settings!, this);
        ThemeService.ThemeChanged += OnThemeChanged;

        SetupTray();

        // 首次加载后直达时间线（在 Loaded 里导航，与旧导航栏的加载时机一致，
        // 避免 Frame 未完成布局时初始化页面）
        ContentFrame.Loaded += (_, _) =>
        {
            if (ContentFrame.Content == null)
                ContentFrame.Navigate(typeof(TimelinePage));
        };

        // 关闭按钮：按设置决定"最小化到托盘"还是"退出程序"
        AppWindow.Closing += OnAppWindowClosing;

        if (App.StartMinimized) HideToTray();
    }

    /// <summary>
    /// 点窗口关闭按钮时的处理。
    /// 注意：WinUI 3 的 Window.Closed 无法取消，必须用 AppWindow.Closing（可 Cancel）。
    /// </summary>
    private void OnAppWindowClosing(Microsoft.UI.Windowing.AppWindow sender,
        Microsoft.UI.Windowing.AppWindowClosingEventArgs args)
    {
        if (App.AllowClose) return; // 已在退出流程中（托盘菜单退出）

        if (App.Settings?.MinimizeToTrayOnClose == true)
        {
            args.Cancel = true;  // 取消关闭
            HideToTray();        // 隐藏到托盘
        }
        else
        {
            args.Cancel = true;              // 先取消，走统一退出流程（含资源释放）
            (App.Current as App)?.ExitApplication();
        }
    }

    // ---------- Mica ----------

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    private void TryEnableMica()
    {
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            int mica = 2;
            DwmSetWindowAttribute(hwnd, 38, ref mica, sizeof(int));
        }
        catch { }
    }

    // ---------- 图标 ----------

    /// <summary>定位 app.ico：优先 exe 同目录（发布版），回退到源码 Assets 目录（开发时）。</summary>
    public static string? ResolveIconPath()
    {
        var exeDir = Path.GetDirectoryName(Environment.ProcessPath);
        if (exeDir != null)
        {
            var p = Path.Combine(exeDir, "app.ico");
            if (File.Exists(p)) return p;
        }
        // 开发态：从 bin/.../win-x64 往上找到工程根下的 Assets/app.ico
        var probe = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 6 && probe != null; i++, probe = probe.Parent)
        {
            var p = Path.Combine(probe.FullName, "Assets", "app.ico");
            if (File.Exists(p)) return p;
        }
        return null;
    }

    /// <summary>
    /// 设置窗口/任务栏图标（WinUI3 必须显式调用，否则标题栏左上角不显示图标）。
    /// 用 AppWindow.SetIcon 指定 ico 文件，系统会按 DPI 自动选帧。
    /// </summary>
    private void ApplyWindowIcon()
    {
        try
        {
            var icoPath = ResolveIconPath();
            if (icoPath == null) { DiagnosticsLog.Warn("未找到 app.ico，窗口图标未设置"); return; }
            AppWindow.SetIcon(icoPath);
        }
        catch (Exception ex)
        {
            DiagnosticsLog.Error("设置窗口图标失败", ex);
        }
    }

    // ---------- 托盘 ----------

    private void SetupTray()
    {
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            _tray = new TrayIconService("电脑日志记录 — 双击打开", ShowFromTray, ShowTrayMenu);
            // 图标：app.ico 按托盘尺寸取帧；加载失败时服务内部回退 exe 内嵌图标
            if (!_tray.TryAdd(ResolveIconPath(), hwnd))
                DiagnosticsLog.Warn("托盘图标添加失败（本会话无托盘图标）");

            // 通知服务接气泡
            _notifier.ShowBalloon = (title, text) =>
                DispatcherQueue.TryEnqueue(() => _tray?.ShowBalloon(title, text));
        }
        catch (Exception ex) { DiagnosticsLog.Error("托盘初始化失败", ex); }
    }

    public void DisposeTray()
    {
        try { _tray?.Dispose(); } catch { }
        _tray = null;
    }

    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);

    public void ShowFromTray()
    {
        try
        {
            Activate();
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            ShowWindow(hwnd, 9);
            SetForegroundWindow(hwnd);
        }
        catch { }
    }

    /// <summary>
    /// 右键托盘图标时弹出菜单（Win32 原生实现）。
    /// 用 CreatePopupMenu + TrackPopupMenuEx 直接创建 Win32 菜单，完全可控：
    /// 窗口隐藏到托盘时同样有效，不依赖任何托盘库的菜单机制。
    /// </summary>
    private void ShowTrayMenu()
    {
        try
        {
            DiagnosticsLog.Info("托盘右键：弹出菜单");
            GetCursorPos(out var pt);

            var hMenu = CreatePopupMenu();
            if (hMenu == IntPtr.Zero) return;

            try
            {
                const uint MF_STRING = 0x0000;
                const uint MF_SEPARATOR = 0x0800;
                const uint MF_CHECKED = 0x0008;
                const uint MF_UNCHECKED = 0x0000;
                const uint TPM_RIGHTBUTTON = 0x0002;
                const uint TPM_RETURNCMD = 0x0100;
                const uint TPM_BOTTOMALIGN = 0x0020;  // 菜单底边对齐光标 Y（向上展开）
                // 只向上展开、不右对齐：菜单左边缘从图标处向右展开（符合托盘习惯）
                const uint TPM_ALIGN = TPM_BOTTOMALIGN;

                const int ID_SHOW = 1;
                const int ID_SETTINGS = 2;
                const int ID_AUTOSTART = 3;
                const int ID_EXIT = 4;

                AppendMenu(hMenu, MF_STRING, ID_SHOW, "显示主窗口");
                AppendMenu(hMenu, MF_STRING, ID_SETTINGS, "设置");
                AppendMenu(hMenu, MF_SEPARATOR, 0, null);

                bool autoStart = AutoStartService.IsEnabled();
                AppendMenu(hMenu, MF_STRING | (autoStart ? MF_CHECKED : MF_UNCHECKED),
                    ID_AUTOSTART, "开机自启动");
                AppendMenu(hMenu, MF_SEPARATOR, 0, null);
                AppendMenu(hMenu, MF_STRING, ID_EXIT, "退出");

                // 必须先让窗口成为前台窗口，否则菜单弹出后点击外部不会关闭（Win32 已知行为）
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                SetForegroundWindow(hwnd);

                // TPM_RETURNCMD：同步返回被点中的菜单项 ID（不发送 WM_COMMAND）
                int cmd = TrackPopupMenuEx(hMenu, TPM_RIGHTBUTTON | TPM_RETURNCMD | TPM_ALIGN,
                    pt.X, pt.Y, hwnd, IntPtr.Zero);

                // 菜单已关闭，按用户选择执行（在 UI 线程同步处理）
                switch (cmd)
                {
                    case ID_SHOW:
                        ShowFromTray();
                        break;
                    case ID_SETTINGS:
                        ShowFromTray();
                        if (ContentFrame.CurrentSourcePageType != typeof(SettingsPage))
                            ContentFrame.Navigate(typeof(SettingsPage));
                        break;
                    case ID_AUTOSTART:
                        AutoStartService.SetEnabled(!autoStart);
                        if (App.Settings != null)
                        {
                            App.Settings.StartWithWindows = !autoStart;
                            App.Settings.Save();
                        }
                        break;
                    case ID_EXIT:
                        DiagnosticsLog.Info("托盘菜单：点击了「退出」");
                        (App.Current as App)?.ExitApplication();
                        break;
                }
            }
            finally
            {
                DestroyMenu(hMenu);
            }
        }
        catch (Exception ex)
        {
            DiagnosticsLog.Error("弹出托盘菜单失败", ex);
        }
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenu(IntPtr hMenu, uint uFlags, int uIDNewItem, string? lpNewItem);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenuEx(IntPtr hMenu, uint fuFlags, int x, int y,
        IntPtr hwnd, IntPtr lptpm);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    /// <summary>隐藏窗口到托盘（public：启动流程在 Activate 后需要调用一次）。</summary>
    public void HideToTray()
    {
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            ShowWindow(hwnd, 0);
        }
        catch { }
    }

    /// <summary>退出时隐藏窗口（让用户立即看到反馈）。</summary>
    public void HideForExit()
    {
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            ShowWindow(hwnd, 0); // SW_HIDE
        }
        catch { }
    }

    // ---------- 主题 ----------

    private void OnThemeChanged()
        => DispatcherQueue.TryEnqueue(() =>
        {
            ThemeService.ApplyTitleBar(this);
            var current = ContentFrame.CurrentSourcePageType;
            if (current != null) ContentFrame.Navigate(current);
        });
}
