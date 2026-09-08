using H.NotifyIcon;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using PCActivityLog.Services;
using PCActivityLog.Views;

using System.Runtime.InteropServices;

namespace PCActivityLog;

/// <summary>
/// 主窗口 —— 左侧 NavigationView 导航 + Mica 背景 + 托盘图标。
/// 托盘用纯代码创建（H.NotifyIcon 在 XAML 中会破坏同文件其他命名元素的字段生成，
/// 这是该库与 WinUI3 XAML 编译器混用时的已知限制）。
/// </summary>
public sealed partial class MainWindow : Window
{
    private readonly NotificationService _notifier;
    private TaskbarIcon? _tray;

    public MainWindow(NotificationService notifier)
    {
        _notifier = notifier;
        InitializeComponent();

        TryEnableMica();
        ApplyWindowIcon(); // 窗口/任务栏图标（WinUI3 需显式设置）
        ThemeService.Apply(App.Settings!, this);
        ThemeService.ThemeChanged += OnThemeChanged;

        SetupTray();

        // 初始页由 XAML 中 NavigationViewItem 的 IsSelected="True" 触发 SelectionChanged 加载；
        // 这里不再重复 Navigate，避免与 SelectionChanged 竞争导致内容与高亮不一致。

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
    private static string? ResolveIconPath()
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

    /// <summary>取托盘图标尺寸（跟随 DPI：100%=16px，150%=24px）。不引 WinForms，直接算。</summary>
    private static System.Drawing.Size GetTrayIconSize()
    {
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance!);
            uint dpi = GetDpiForWindow(hwnd);
            var px = dpi == 0 ? 16 : (int)Math.Round(16.0 * dpi / 96.0);
            return new System.Drawing.Size(px, px);
        }
        catch { return new System.Drawing.Size(16, 16); }
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    // ---------- 托盘（纯代码） ----------

    private void SetupTray()
    {
        try
        {
            _tray = new TaskbarIcon
            {
                ToolTipText = "电脑日志记录 — 双击打开",
                NoLeftClickDelay = true,
            };

            // 图标：优先用 app.ico 精确取托盘尺寸帧（16/24px），保证小图标清晰；
            // ExtractAssociatedIcon 只取单一尺寸且不按 DPI 选帧，小图标会发虚。
            try
            {
                var icoPath = ResolveIconPath();
                if (icoPath != null && File.Exists(icoPath))
                {
                    // 按系统托盘图标尺寸取帧（100% DPI=16px，150%=24px）
                    var size = GetTrayIconSize();
                    _tray.Icon = new System.Drawing.Icon(icoPath, size);
                }
                else
                {
                    var exePath = Environment.ProcessPath;
                    if (exePath != null)
                        _tray.Icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
                }
            }
            catch (Exception ex)
            {
                DiagnosticsLog.Warn("托盘图标加载失败，尝试 exe 关联图标: " + ex.Message);
                try
                {
                    var exePath = Environment.ProcessPath;
                    if (exePath != null) _tray.Icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
                }
                catch (Exception ex2) { DiagnosticsLog.Error("托盘图标回退也失败", ex2); }
            }

            var menu = new MenuFlyout();
            var show = new MenuFlyoutItem { Text = "显示主窗口" };
            show.Click += (_, _) => ShowFromTray();
            menu.Items.Add(show);

            var settings = new MenuFlyoutItem { Text = "设置" };
            settings.Click += (_, _) => { ShowFromTray(); Nav.SelectedItem = Nav.MenuItems[2]; };
            menu.Items.Add(settings);

            var autoStart = new ToggleMenuFlyoutItem
            {
                Text = "开机自启动",
                IsChecked = AutoStartService.IsEnabled(),
            };
            autoStart.Click += (_, _) =>
            {
                AutoStartService.SetEnabled(autoStart.IsChecked);
                if (App.Settings != null) { App.Settings.StartWithWindows = autoStart.IsChecked; App.Settings.Save(); }
            };
            menu.Items.Add(autoStart);

            menu.Items.Add(new MenuFlyoutSeparator());

            var exit = new MenuFlyoutItem { Text = "退出" };
            exit.Click += (_, _) => (App.Current as App)?.ExitApplication();
            menu.Items.Add(exit);

            _tray.ContextFlyout = menu;
            _tray.LeftClickCommand = new RelayCommand(ShowFromTray);
            _tray.ForceCreate();

            // 通知服务接气泡
            _notifier.ShowBalloon = (title, text) =>
                DispatcherQueue.TryEnqueue(() =>
                {
                    try { _tray?.ShowNotification(title, text); } catch { }
                });
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

    private void HideToTray()
    {
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            ShowWindow(hwnd, 0);
        }
        catch { }
    }

    // ---------- 导航 ----------

    private void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem item) return;
        Type? page = item.Tag as string switch
        {
            "timeline" => typeof(TimelinePage),
            "stats" => typeof(StatsPage),
            "settings" => typeof(SettingsPage),
            _ => null,
        };
        if (page != null && ContentFrame.CurrentSourcePageType != page)
            ContentFrame.Navigate(page);
    }

    private void OnThemeChanged()
        => DispatcherQueue.TryEnqueue(() =>
        {
            ThemeService.ApplyTitleBar(this);
            var current = ContentFrame.CurrentSourcePageType;
            if (current != null) ContentFrame.Navigate(current);
        });
}
