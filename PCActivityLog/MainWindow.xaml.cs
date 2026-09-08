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
        ThemeService.Apply(App.Settings!, this);
        ThemeService.ThemeChanged += OnThemeChanged;

        SetupTray();

        // 启动即加载时间线页（Frame 默认空白，必须显式导航）
        ContentFrame.Navigate(typeof(TimelinePage));

        if (App.StartMinimized) HideToTray();
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

            // 图标：从 exe 关联图标加载
            try
            {
                var exePath = Environment.ProcessPath;
                if (exePath != null)
                    _tray.Icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
            }
            catch { }

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
