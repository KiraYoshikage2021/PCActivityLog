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

        // 日期按钮文本随 Vm.DateFrom/DateTo 变化刷新（ApplyRange、日历选择都会走这里）
        Vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(Vm.DateFrom) or nameof(Vm.DateTo)) SyncDateButtons();
        };

        // 响应式工具栏：窗口宽度不足时把日期区间与按钮组搬到第二行（独立列宽），
        // 避免单行溢出被窗口边缘裁剪（SettingsPage 同款响应式模式）
        SizeChanged += (_, e) => ApplyToolbarLayout(e.NewSize.Width);

        // 用户滚动/点击列表时通知 VM 避让自动刷新（刷新会重建集合打断滚动）
        EventList.AddHandler(PointerWheelChangedEvent,
            (PointerEventHandler)((_, _) => Vm.NotifyUserActive()), true);
        EventList.AddHandler(PointerPressedEvent,
            (PointerEventHandler)((_, _) => Vm.NotifyUserActive()), true);

        // 页面被 Frame 缓存，Loaded 会多次触发：首次才全量查库，之后只轻量补刷
        Loaded += (_, _) =>
        {
            ApplyToolbarLayout(ActualWidth); // 初次布局/缓存恢复后按当前宽度归位
            SetupDatePickers();              // 挂接日历事件并同步初始显示（订阅前 VM 已初始化）
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

    // ---------- 响应式工具栏（单行 ⇄ 两行） ----------

    /// <summary>单行工具栏（含左右内边距）所需的最小窗口宽度；不足则折两行。</summary>
    private const double ToolbarSingleRowMinWidth = 1310;

    /// <summary>当前是否已处于两行布局（避免重复搬移）。</summary>
    private bool _toolbarWrapped;

    private void ApplyToolbarLayout(double width)
    {
        if (double.IsNaN(width) || width <= 0) return;
        bool wrap = width < ToolbarSingleRowMinWidth;
        if (wrap == _toolbarWrapped) return;
        _toolbarWrapped = wrap;

        if (wrap)
        {
            // 两行：日期区间与按钮组搬进 DateRow（列宽独立，不再继承第一行分组/搜索列的宽度）
            MoveInto(DateRow, 0, DateFromLabel);
            MoveInto(DateRow, 1, DateFromButton);
            MoveInto(DateRow, 2, DateToLabel);
            MoveInto(DateRow, 3, DateToButton);
            MoveInto(DateRow, 5, ActionButtons);
            DateRow.Visibility = Visibility.Visible;
        }
        else
        {
            // 单行：搬回 FilterRow 的原始列位
            MoveInto(FilterRow, 3, DateFromLabel);
            MoveInto(FilterRow, 4, DateFromButton);
            MoveInto(FilterRow, 5, DateToLabel);
            MoveInto(FilterRow, 6, DateToButton);
            MoveInto(FilterRow, 8, ActionButtons);
            DateRow.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>把元素搬进目标 Grid 的指定列（元素可能已在源 Grid 或另一个 Grid 中）。</summary>
    private static void MoveInto(Grid target, int column, FrameworkElement element)
    {
        if (element.Parent is Grid oldGrid)
            oldGrid.Children.Remove(element);
        element.SetValue(Grid.RowProperty, 0);
        element.SetValue(Grid.ColumnProperty, column);
        target.Children.Add(element);
    }

    // ---------- 日期选择（日期按钮 + CalendarView 日历下拉） ----------

    // 注意：日历的 Opening/SelectedDatesChanged 不写在 XAML 里——事件特性写在
    // Button.Flyout 内容上会触发 XamlCompiler 静默崩溃（pass1 产出空 .g.cs），
    // 改在 Loaded 里编程挂接。
    private bool _syncingFromDate;
    private bool _syncingToDate;
    private CalendarView? _fromCalendar;
    private CalendarView? _toCalendar;

    /// <summary>日期按钮的显示文本（随 Vm.DateFrom/DateTo 变化刷新）。</summary>
    private string FormatDate(DateTimeOffset? d) => d?.ToString("yyyy-MM-dd") ?? "选择日期";

    /// <summary>挂接两个日历的事件。</summary>
    private void SetupDatePickers()
    {
        _fromCalendar = DateFromButton.Flyout is Flyout ff && ff.Content is CalendarView c1 ? c1 : null;
        _toCalendar = DateToButton.Flyout is Flyout tf && tf.Content is CalendarView c2 ? c2 : null;
        if (_fromCalendar == null || _toCalendar == null
            || DateFromButton.Flyout is not Flyout fromFlyout
            || DateToButton.Flyout is not Flyout toFlyout)
            return;

        fromFlyout.Opening += (_, _) => SyncCalendarTo(_fromCalendar!, Vm.DateFrom, from: true);
        toFlyout.Opening += (_, _) => SyncCalendarTo(_toCalendar!, Vm.DateTo, from: false);

        _fromCalendar.SelectedDatesChanged += FromCalendar_SelectedDatesChanged;
        _toCalendar.SelectedDatesChanged += ToCalendar_SelectedDatesChanged;

        SyncDateButtons();
    }

    /// <summary>日历打开时同步显示与选中当前筛选值（同步期间抑制回写与收起）。</summary>
    private void SyncCalendarTo(CalendarView cal, DateTimeOffset? current, bool from)
    {
        if (from) _syncingFromDate = true; else _syncingToDate = true;
        cal.SetDisplayDate((current ?? DateTimeOffset.Now).DateTime);
        cal.SelectedDates.Clear();
        cal.SelectedDates.Add((current ?? DateTimeOffset.Now).DateTime);
        if (from) _syncingFromDate = false; else _syncingToDate = false;
    }

    /// <summary>刷新日期按钮文本。</summary>
    private void SyncDateButtons()
    {
        DateFromButton.Content = FormatDate(Vm.DateFrom);
        DateToButton.Content = FormatDate(Vm.DateTo);
    }

    private void FromCalendar_SelectedDatesChanged(CalendarView sender, CalendarViewSelectedDatesChangedEventArgs args)
    {
        // WinUI 3 的事件参数没有 AddedItems（UWP 才有），从 sender.SelectedDates 读取
        if (_syncingFromDate || sender.SelectedDates.Count == 0) return;
        Vm.DateFrom = sender.SelectedDates[0]; // 触发 Vm 自动刷新
        SyncDateButtons();
        ((Flyout)DateFromButton.Flyout!).Hide();
    }

    private void ToCalendar_SelectedDatesChanged(CalendarView sender, CalendarViewSelectedDatesChangedEventArgs args)
    {
        if (_syncingToDate || sender.SelectedDates.Count == 0) return;
        Vm.DateTo = sender.SelectedDates[0];
        SyncDateButtons();
        ((Flyout)DateToButton.Flyout!).Hide();
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
