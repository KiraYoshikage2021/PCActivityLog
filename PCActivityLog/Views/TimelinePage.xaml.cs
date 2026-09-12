using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using PCActivityLog.Models;
using PCActivityLog.Services;
using PCActivityLog.ViewModels;

namespace PCActivityLog.Views;

/// <summary>
/// 时间线页 —— 原生 ListView 事件列表（虚拟化性能最优）。
/// 已从 CommunityToolkit DataGrid 迁移：ListView 原生虚拟化，滚动流畅；
/// 代价是放弃列宽拖拽与表头排序（静态表头，纯视觉分栏）。
/// </summary>
public sealed partial class TimelinePage : Page
{
    public TimelineViewModel Vm { get; }

    /// <summary>是否已完成首次数据加载（页面被 Frame 缓存，切回来时只做轻量补刷）。</summary>
    private bool _initialized;

    public TimelinePage()
    {
        Vm = new TimelineViewModel(App.Db!, App.WriteQueueInstance!, new ExportService(App.Db!));
        App.TimelinePageVm = Vm; // 供退出时统一 Dispose（页面被缓存复用）
        InitializeComponent();

        Vm.ShowMessage = async (text, title) =>
        {
            var dlg = new ContentDialog
            {
                Title = title,
                Content = text,
                CloseButtonText = "确定",
                XamlRoot = XamlRoot,
            };
            await dlg.ShowAsync();
        };
        Vm.SelectRequested = OnSelectRequested;

        // 用户滚动/点击列表时通知 VM 避让自动刷新（刷新会重建集合打断滚动）
        EventList.AddHandler(PointerWheelChangedEvent,
            (PointerEventHandler)((_, _) => Vm.NotifyUserActive()), true);
        EventList.AddHandler(PointerPressedEvent,
            (PointerEventHandler)((_, _) => Vm.NotifyUserActive()), true);

        // 页面被 Frame 缓存，Loaded 会多次触发：首次才全量查库，之后只轻量补刷
        Loaded += (_, _) =>
        {
            if (!_initialized)
            {
                _initialized = true;
                Vm.Refresh();
            }
            else
            {
                Vm.RefreshIfIdle();
            }
        };
    }

    // ---------- 行交互 ----------

    private void OnSelectRequested(int index)
    {
        if (index < 0 || index >= Vm.Items.Count) return;
        EventList.SelectedIndex = index;
        EventList.ScrollIntoView(Vm.Items[index]);
    }

    /// <summary>双击行：有关联下载 → 跳转；否则打开位置。</summary>
    private async void EventList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        => await Vm.OpenOrJumpAsync(EventList.SelectedItem as ActivityEventItem);

    /// <summary>右键即选中所在行（ListView 右键默认不改选中，需手动同步）。</summary>
    private void EventList_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is ActivityEventItem item)
            EventList.SelectedItem = item;
    }

    private void EventList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // 选中项同步到 VM（右键菜单等场景读取）
        if (e.AddedItems.Count > 0)
            Vm.SelectedItem = e.AddedItems[0] as ActivityEventItem;
    }

    private async void OpenLocation_Click(object sender, RoutedEventArgs e)
        => await Vm.OpenLocationAsync(EventList.SelectedItem as ActivityEventItem);

    private void JumpLinked_Click(object sender, RoutedEventArgs e)
        => Vm.JumpLinked(EventList.SelectedItem as ActivityEventItem);

    private void CopyName_Click(object sender, RoutedEventArgs e)
        => Vm.CopyName(EventList.SelectedItem as ActivityEventItem);

    // ---------- 导出 ----------

    private async void ExportCsv_Click(object sender, RoutedEventArgs e) => await Vm.ExportCsvAsync();
    private async void ExportJson_Click(object sender, RoutedEventArgs e) => await Vm.ExportJsonAsync();

    // ---------- 设置入口 ----------

    /// <summary>工具栏齿轮按钮：进入设置页（本程序已无导航栏，设置由此进入）。</summary>
    private void OpenSettings_Click(object sender, RoutedEventArgs e)
        => Frame.Navigate(typeof(SettingsPage));

    // ---------- 手动添加记录 ----------

    private async void AddEvent_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AddEventDialog { XamlRoot = XamlRoot };
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary && dialog.Result != null)
            App.WriteQueueInstance?.Enqueue(dialog.Result);
    }
}
