using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using PCActivityLog.Services;
using PCActivityLog.ViewModels;

namespace PCActivityLog.Views;

/// <summary>
/// 主窗口 —— 时间线 + 统计两个页签。
/// 关闭按钮的行为由 App 按设置控制（最小化到托盘或退出）。
/// 窗口过程挂钩监听 WM_SETTINGCHANGE(ImmersiveColorSet)，实现跟随系统浅/深主题实时切换。
/// </summary>
public partial class MainWindow : Window
{
    private const int WM_SETTINGCHANGE = 0x001A;

    private readonly MainViewModel _vm;

    public MainViewModel Vm => _vm;

    public MainWindow(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        vm.SelectRequested += OnSelectRequested;

        // 从隐藏（托盘）重新变为可见时补刷：托盘期间到达的事件一打开就能看到
        IsVisibleChanged += (_, e) =>
        {
            if ((bool)e.NewValue) _vm.RefreshIfIdle();
        };
    }

    /// <summary>窗口被激活（用户切回本软件）时补刷，保证"看它的时候总是新的"。</summary>
    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        _vm.RefreshIfIdle();
    }

    /// <summary>窗口句柄就绪：应用标题栏主题 + 挂系统消息钩子。</summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ThemeService.ApplyTitleBar(this);
        if (PresentationSource.FromVisual(this) is HwndSource src)
            src.AddHook(WndProc);
    }

    /// <summary>
    /// 系统消息钩子。性能说明：非 WM_SETTINGCHANGE 消息只做一次整数比较即返回，
    /// 稳态开销纳秒级；仅当 lParam 指向 "ImmersiveColorSet"（用户切换了系统主题）才走主题切换。
    /// </summary>
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_SETTINGCHANGE && App.Settings?.ThemeMode == "auto")
        {
            try
            {
                if (Marshal.PtrToStringUni(lParam) == "ImmersiveColorSet")
                    ThemeService.Apply(App.Settings);
            }
            catch { /* 消息参数异常时忽略 */ }
        }
        return IntPtr.Zero;
    }

    /// <summary>VM 请求选中行：滚动到目标并高亮。</summary>
    private void OnSelectRequested(int index)
    {
        if (index < 0 || index >= Items.Count) return;
        Grid.SelectedIndex = index;
        Grid.ScrollIntoView(Items[index]);
    }

    private System.Collections.ObjectModel.ObservableCollection<ActivityEventItem> Items => _vm.Items;

    /// <summary>切换页签：进入统计页时刷新统计数据。</summary>
    private void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count > 0 && e.AddedItems[0] is TabItem tab && tab.Header is string h && h.Contains("统计"))
            _vm.Stats?.Refresh();
    }

    /// <summary>双击行：跳转关联或打开位置。</summary>
    private void Grid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        => _vm.OpenOrJump(Grid.SelectedItem as ActivityEventItem);

    /// <summary>右键菜单：复制名称。</summary>
    private void CopyName_Click(object sender, RoutedEventArgs e)
    {
        if (Grid.SelectedItem is ActivityEventItem item)
            Clipboard.SetText(item.Name);
    }

    /// <summary>工具栏：打开设置窗口。</summary>
    private void OpenSettings_Click(object sender, RoutedEventArgs e)
        => App.ShowSettingsWindow();

    /// <summary>窗口关闭：按设置决定是最小化到托盘还是退出程序（真正的收尾由 App.OnExit 统一做）。</summary>
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        base.OnClosing(e);
        if (App.AllowClose) return; // 已在退出流程中（托盘菜单退出）

        if (App.Settings?.MinimizeToTrayOnClose == true)
        {
            e.Cancel = true;
            Hide();
            App.OnWindowHiddenToTray();
        }
        else
        {
            // 用户关闭了"最小化到托盘"选项 → 点 X 就是退出程序
            App.AllowClose = true;
            Application.Current.Shutdown();
        }
    }
}
