using Microsoft.UI.Xaml;
using Microsoft.UI.Windowing;
using Microsoft.UI.Dispatching;
using PCActivityLog.Services;
using PCActivityLog.Views;

using System.Runtime.InteropServices;

namespace PCActivityLog;

/// <summary>
/// 主窗口 —— 时间线铺满窗口（无导航栏），设置从时间线工具栏齿轮进入。
/// 托盘为 Windows 原生 Shell_NotifyIcon（TrayIconService），托盘菜单为 Win32 原生实现。
/// 窗口尺寸：恢复上次几何（含工作区钳制）、最小尺寸约束、几何持久化。
/// </summary>
public sealed partial class MainWindow : Window
{
    private readonly NotificationService _notifier;
    private TrayIconService? _tray;

    public MainWindow(NotificationService notifier)
    {
        _notifier = notifier;
        InitializeComponent();

        ApplyWindowIcon(); // 窗口/任务栏图标（WinUI3 需显式设置）
        // 尺寸恢复/最小尺寸/几何持久化必须等内容 Loaded：
        // WinUI 在窗口首次布局阶段会用框架默认几何覆盖构造函数/Activated 里做的设置
        //（实测 MoveAndResize 的位置在首次激活后仍被改写），内容 Loaded 之后再设置才稳定。
        if (Content is FrameworkElement root)
            root.Loaded += OnRootLoaded;
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

    // ---------- 窗口尺寸与位置 ----------

    /// <summary>内容 Loaded（框架默认几何已套用完毕）后恢复/设置窗口几何，只处理一次。</summary>
    private void OnRootLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe) fe.Loaded -= OnRootLoaded;
        RestoreOrApplyWindowBounds();
        ApplyMinimumSize();
        SetupGeometryPersistence();

        // 启动后落一次盘：保证恢复/默认几何被持久化（不依赖 Changed 事件是否触发）
        try
        {
            var t = DispatcherQueue.CreateTimer();
            t.Interval = TimeSpan.FromMilliseconds(800);
            t.IsRepeating = false;
            t.Tick += (_, _) => SaveWindowGeometry();
            t.Start();
        }
        catch { }
    }

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;

    // 初始默认客户区（有效像素）。首次运行或上次记录无效时使用；
    // 按显示器 DPI 换算为物理像素，并被钳制到当前工作区内（小屏不超界）。
    private const double DefaultClientWidth = 1320;
    private const double DefaultClientHeight = 800;

    // 最小窗口客户区（有效像素）。低于该宽度时工具栏已自动折叠为两行 + 横向滚动兜底，
    // 窗口本身不允许缩得更小（否则按钮/内容不可达）。
    private const double MinClientWidth = 800;
    private const double MinClientHeight = 480;

    private DispatcherQueueTimer? _geometrySaveTimer;

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    /// <summary>当前 DPI 缩放系数（物理像素 / 有效像素）；取不到按 100%。</summary>
    private double GetDpiScale()
    {
        try
        {
            uint dpi = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this));
            return dpi > 0 ? dpi / 96.0 : 1.0;
        }
        catch { return 1.0; }
    }

    /// <summary>
    /// 恢复上次的窗口大小与位置；首次运行或记录无效时用默认尺寸并居中。
    /// 全程用 SetWindowPos 直接改 Win32 窗口（AppWindow 的尺寸 API 在启动阶段时序不稳）。
    /// 恢复后钳制到当前显示器工作区（换显示器/改缩放后不会跑出屏幕外）。
    /// </summary>
    private void RestoreOrApplyWindowBounds()
    {
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var s = App.Settings;

            // 用"当前外框 - 当前客户区"差值推算非客户区边框（标题栏+边框），
            // 免维护 DPI 换算表，任何缩放下都准确
            GetClientRect(hwnd, out var rc);
            var outerNow = AppWindow.Size;
            int chromeW = outerNow.Width - (rc.Right - rc.Left);
            int chromeH = outerNow.Height - (rc.Bottom - rc.Top);

            int x, y, w, h; // 物理像素（外框单位）
            bool restored = s?.WindowBoundsValid == true && s.WindowWidth >= 320 && s.WindowHeight >= 240;

            if (restored)
            {
                x = s!.WindowLeft; y = s.WindowTop;
                w = s.WindowWidth; h = s.WindowHeight;
            }
            else
            {
                double scale = GetDpiScale();
                var area0 = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
                int minW = (int)Math.Round(MinClientWidth * scale);
                int minH = (int)Math.Round(MinClientHeight * scale);
                w = Math.Clamp((int)Math.Round(DefaultClientWidth * scale), minW, Math.Max(minW, area0.Width)) + chromeW;
                h = Math.Clamp((int)Math.Round(DefaultClientHeight * scale), minH, Math.Max(minH, area0.Height)) + chromeH;
                x = area0.X + Math.Max(0, (area0.Width - w) / 2);
                y = area0.Y + Math.Max(0, (area0.Height - h) / 2);
            }

            SetWindowPos(hwnd, IntPtr.Zero, x, y, w, h, SWP_NOZORDER | SWP_NOACTIVATE);

            // 落位后按窗口实际所在显示器钳制（保存时的显示器可能已断开/改缩放）
            double scaleNow = GetDpiScale();
            var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
            ClampWindowInto(area,
                (int)Math.Round(MinClientWidth * scaleNow) + chromeW,
                (int)Math.Round(MinClientHeight * scaleNow) + chromeH);

            if (restored && s!.WindowMaximized && AppWindow.Presenter is OverlappedPresenter p)
                p.Maximize();
        }
        catch (Exception ex)
        {
            DiagnosticsLog.Error("恢复窗口尺寸失败", ex);
        }
    }

    /// <summary>把窗口钳回工作区：尺寸不小于最小值、不大于工作区，位置至少露出部分标题栏。</summary>
    private void ClampWindowInto(Windows.Graphics.RectInt32 area, int minW, int minH)
    {
        var pos = AppWindow.Position;
        var size = AppWindow.Size;
        int w = Math.Clamp(size.Width, minW, Math.Max(minW, area.Width));
        int h = Math.Clamp(size.Height, minH, Math.Max(minH, area.Height));
        int x = Math.Clamp(pos.X, area.X - w + 120, area.X + area.Width - 120);
        int y = Math.Clamp(pos.Y, area.Y, area.Y + area.Height - 80);

        if (w != size.Width || h != size.Height)
            AppWindow.Resize(new Windows.Graphics.SizeInt32(w, h));
        if (x != pos.X || y != pos.Y)
            AppWindow.Move(new Windows.Graphics.PointInt32(x, y));
    }

    /// <summary>
    /// 设置最小窗口尺寸（WinUI 的 Window 没有 MinWidth/MinHeight）。
    /// 走 OverlappedPresenter.PreferredMinimumWidth/Height；已知上游缺陷
    /// microsoft-ui-xaml#10452：该值不随 DPI 缩放（按物理像素解释），故按当前 DPI 换算后赋值。
    /// </summary>
    private void ApplyMinimumSize()
    {
        try
        {
            if (AppWindow.Presenter is OverlappedPresenter presenter)
            {
                double scale = GetDpiScale();
                presenter.PreferredMinimumWidth = (int)Math.Round(MinClientWidth * scale);
                presenter.PreferredMinimumHeight = (int)Math.Round(MinClientHeight * scale);
            }
        }
        catch (Exception ex)
        {
            DiagnosticsLog.Warn("设置最小窗口尺寸失败: " + ex.Message);
        }
    }

    /// <summary>监听窗口移动/缩放，防抖 400ms 后把几何写入设置（连续拖动只落盘最后一次）。</summary>
    private void SetupGeometryPersistence()
    {
        AppWindow.Changed += (_, args) =>
        {
            if (args.DidPositionChange || args.DidSizeChange)
                ScheduleGeometrySave();
        };
    }

    private void ScheduleGeometrySave()
    {
        try
        {
            if (_geometrySaveTimer == null)
            {
                _geometrySaveTimer = DispatcherQueue.CreateTimer();
                _geometrySaveTimer.Interval = TimeSpan.FromMilliseconds(400);
                _geometrySaveTimer.IsRepeating = false;
                _geometrySaveTimer.Tick += (_, _) => SaveWindowGeometry();
            }
            _geometrySaveTimer.Start(); // 重复 Start 会重置倒计时
        }
        catch { }
    }

    /// <summary>立即保存窗口几何（防抖计时器回调；退出流程也会直接调用）。最大化时不覆盖上次的正常几何。</summary>
    public void SaveWindowGeometry()
    {
        try
        {
            var s = App.Settings;
            if (s == null) return;

            bool maximized = AppWindow.Presenter is OverlappedPresenter p
                             && p.State == OverlappedPresenterState.Maximized;
            s.WindowMaximized = maximized;
            if (!maximized)
            {
                s.WindowLeft = AppWindow.Position.X;
                s.WindowTop = AppWindow.Position.Y;
                s.WindowWidth = AppWindow.Size.Width;   // 物理像素
                s.WindowHeight = AppWindow.Size.Height;
            }
            s.WindowBoundsValid = true;
            s.Save();
        }
        catch (Exception ex)
        {
            DiagnosticsLog.Error("保存窗口几何失败", ex);
        }
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
