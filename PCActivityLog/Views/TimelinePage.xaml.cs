using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using PCActivityLog.Models;
using PCActivityLog.Services;
using PCActivityLog.ViewModels;
using Windows.Foundation;

namespace PCActivityLog.Views;

/// <summary>
/// 时间线页 —— 原生 ListView 事件列表（虚拟化性能最优）。
/// 已从 CommunityToolkit DataGrid 迁移：ListView 原生虚拟化，滚动流畅；
/// 表头为静态视觉分栏（不支持排序），列宽由表头 GridSplitter 拖拽调整并记忆。
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
        SetupColumnWidths();

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
            HookHeaderScrollSync();          // 表头↔列表横向滚动同步（Loaded 多次触发，内部有护栏）
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

    // ---------- 列宽（表头拖拽调宽 + 记忆） ----------

    // 模型：GridSplitter 直接改写表头 ColumnDefinition.Width → 变化回调里钳制并同步到
    // 所有已实现（可见）的行模板；行在虚拟化实现时再按当前列宽对齐一次。列宽防抖落盘。
    // 名称列恒为 ★ 填充剩余空间，不参与固定宽度，也无需持久化。

    /// <summary>当前列宽（像素），表头与行模板的唯一宽度来源。</summary>
    private readonly double[] _colWidths = (double[])TimelineColumns.Defaults.Clone();

    /// <summary>表头 6 个 ColumnDefinition（与列下标一一对应）。</summary>
    private ColumnDefinition[] _headerCols = null!;

    /// <summary>同步/钳制期间抑制宽度变化回调重入。</summary>
    private bool _syncingColumns;

    /// <summary>列宽持久化防抖（拖动过程中不落盘）。</summary>
    private DispatcherQueueTimer? _colSaveTimer;

    // ---------- 横向滚动（窗口/列宽超出可视区时整表可左右滚动） ----------

    // 名称★列压到下限（120）后表格总宽即固定，此时列表底部出现横向滚动条；
    // 表头内容层用位移变换跟随列表的横向偏移，并按卡片宽度裁剪，形成"整表滚动"观感。
    // 表头内容宽度取 列表视口宽（不含竖向滚动条），与行宽天然一致——顺带修复了
    // 竖向滚动条出现时行比表头窄 ~12px 的历史错位。

    /// <summary>行/表头内容左右 Padding 合计（两处均为 10,0）。</summary>
    private const double RowHPadding = 20;

    /// <summary>表格总宽下限 = 固定列合计 + 名称列（★态取下限 / 像素态取实际宽）+ 行内边距。行/行容器/表头的 MinWidth。</summary>
    private double _minTableWidth;

    /// <summary>横向滚动状态：表头名称列由★切为像素宽（拖 2|3 分隔条走"本列伸缩"分支，边界跟手）。</summary>
    private bool _nameIsFixed;

    /// <summary>滚动状态下名称列的像素宽（进入该状态时从下限 120 起，可拖宽）。</summary>
    private double _nameFixedWidth;

    /// <summary>列表内部的 ScrollViewer（横向偏移与视口宽的来源）。</summary>
    private ScrollViewer? _listScroller;

    /// <summary>表头内容层的横向位移（跟随列表滚动）。</summary>
    private readonly TranslateTransform _headerShift = new();

    /// <summary>Loaded 挂接护栏（页面缓存导致 Loaded 多次触发）。</summary>
    private bool _headerSyncHooked;

    /// <summary>挂接表头↔列表滚动同步与表头裁剪。须在视觉树构建后调用（Loaded）。</summary>
    private void HookHeaderScrollSync()
    {
        if (!_headerSyncHooked)
        {
            _headerSyncHooked = true;
            HeaderContent.RenderTransform = _headerShift;
            EventList.SizeChanged += (_, _) => UpdateHeaderSync();
            HeaderClip.SizeChanged += (_, _) => HeaderClip.Clip = new RectangleGeometry
            {
                Rect = new Rect(0, 0, HeaderClip.ActualWidth, HeaderClip.ActualHeight),
            };
        }

        // 列表模板里的 ScrollViewer 首次布局后才存在
        if (_listScroller == null && EventList.ItemsPanelRoot != null)
        {
            _listScroller = FindDescendant<ScrollViewer>(EventList);
            if (_listScroller != null)
            {
                _listScroller.ViewChanged += (_, _) => UpdateHeaderSync();
                _listScroller.SizeChanged += (_, _) => UpdateHeaderSync();
            }
        }
        UpdateHeaderSync();
    }

    /// <summary>表头同步：进出横向滚动状态时切换名称列 ★↔像素，表头内容宽度 = max(表格总宽, 视口宽)，
    /// 并把横向偏移同步为表头位移。</summary>
    private void UpdateHeaderSync()
    {
        var viewport = _listScroller?.ViewportWidth ?? 0;
        if (viewport <= 0) viewport = EventList.ActualWidth;

        // ★填充态的总宽下限（判断是否进入横向滚动状态的基准）
        var floorTotal = RowHPadding + TimelineColumns.Minimums[TimelineColumns.NameColumn];
        for (int i = 0; i < TimelineColumns.Count; i++)
            if (i != TimelineColumns.NameColumn) floorTotal += _colWidths[i];

        // 进入滚动状态：名称列改像素宽（从下限起）。此后拖 2|3 分隔条走"左列伸缩"分支，边界跟手。
        // 若保持★，分隔条会去压右邻列，而★列无空间可填 → 被拖边界不动、右邻列右缘左移的怪象
        if (!_nameIsFixed && viewport > 0 && viewport < floorTotal)
        {
            _nameIsFixed = true;
            _nameFixedWidth = TimelineColumns.Minimums[TimelineColumns.NameColumn];
            SetNameWidthSilently(new GridLength(_nameFixedWidth));
        }
        // 回到宽窗口：名称列恢复★填充
        else if (_nameIsFixed && viewport >= floorTotal)
        {
            _nameIsFixed = false;
            SetNameWidthSilently(new GridLength(1, GridUnitType.Star));
        }

        _minTableWidth = floorTotal;
        if (_nameIsFixed)
            _minTableWidth += _nameFixedWidth - TimelineColumns.Minimums[TimelineColumns.NameColumn];

        if (viewport > 0) HeaderContent.Width = Math.Max(_minTableWidth, viewport);
        _headerShift.X = -(_listScroller?.HorizontalOffset ?? 0);
    }

    /// <summary>静默改写表头名称列宽（抑制宽度变化回调——状态切换本身就是同步流程的一部分）。</summary>
    private void SetNameWidthSilently(GridLength width)
    {
        _syncingColumns = true;
        try { ColName.Width = width; } finally { _syncingColumns = false; }
    }

    /// <summary>表头/行全量同步（列宽变化或名称列状态切换后调用）：先表头（含总宽重算）再所有已实现的行/行容器。</summary>
    private void UpdateTableWidths()
    {
        UpdateHeaderSync();
        if (EventList.ItemsPanelRoot == null) return;
        foreach (var child in EventList.ItemsPanelRoot.Children)
            if (child is ListViewItem item)
                ApplyWidthsToContainer(item);
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : class
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T t) return t;
            var found = FindDescendant<T>(child);
            if (found != null) return found;
        }
        return null;
    }

    private void SetupColumnWidths()
    {
        _headerCols = new[] { ColTime, ColType, ColName, ColDetail, ColLocation, ColNote };

        var widths = TimelineColumns.FromPersisted(App.Settings?.TimelineColumnWidths);
        _syncingColumns = true; // 初次赋值只写表头，不触发同步回调与落盘
        try
        {
            for (int i = 0; i < TimelineColumns.Count; i++)
            {
                _colWidths[i] = widths[i];
                var def = _headerCols[i];
                if (i != TimelineColumns.NameColumn)
                    def.Width = new GridLength(widths[i]);
                def.RegisterPropertyChangedCallback(ColumnDefinition.WidthProperty, OnHeaderColumnWidthChanged);
            }
        }
        finally { _syncingColumns = false; }

        // 虚拟化：行实现/回收时按当前列宽与表格总宽对齐（拖动后滚出的新行不会错位）
        EventList.ContainerContentChanging += (_, e) => ApplyWidthsToContainer((ListViewItem)e.ItemContainer);
        UpdateTableWidths();
    }

    /// <summary>表头某列宽度变化（拖拽/复位）：钳制到最小列宽后同步到所有已实现的行。</summary>
    private void OnHeaderColumnWidthChanged(DependencyObject sender, DependencyProperty dp)
    {
        if (_syncingColumns || sender is not ColumnDefinition def) return;
        var idx = Array.IndexOf(_headerCols, def);
        if (idx < 0) return;

        // 名称列只在滚动状态（像素宽）下会被分隔条改写；★↔像素的状态切换走 SetNameWidthSilently
        if (idx == TimelineColumns.NameColumn)
        {
            if (!_nameIsFixed || def.Width.GridUnitType != GridUnitType.Pixel) return;
            _nameFixedWidth = TimelineColumns.Clamp(TimelineColumns.NameColumn, def.Width.Value);
            _syncingColumns = true;
            try { UpdateTableWidths(); } finally { _syncingColumns = false; }
            return;
        }

        var w = TimelineColumns.Clamp(idx,
            def.Width.GridUnitType == GridUnitType.Pixel ? def.Width.Value : _colWidths[idx]);
        _syncingColumns = true;
        try
        {
            // 拖过头（低于最小列宽）时钳回；重入的回调因 _syncingColumns 直接返回
            if (def.Width.GridUnitType == GridUnitType.Pixel && Math.Abs(def.Width.Value - w) > 0.01)
                def.Width = new GridLength(w);
            _colWidths[idx] = w;
            UpdateTableWidths();
            ScheduleColumnSave();
        }
        finally { _syncingColumns = false; }
    }

    /// <summary>按当前列宽与表格总宽对齐单个行容器（含行模板 Grid；名称★列不动）。</summary>
    private void ApplyWidthsToContainer(ListViewItem container)
    {
        container.MinWidth = _minTableWidth;
        if (container.ContentTemplateRoot is not Grid row) return;
        row.MinWidth = _minTableWidth;
        var defs = row.ColumnDefinitions;
        for (int i = 0; i < TimelineColumns.Count && i < defs.Count; i++)
            if (i != TimelineColumns.NameColumn)
                defs[i].Width = new GridLength(_colWidths[i]);
    }

    /// <summary>拖动停止 0.5s 后落盘（防抖，避免拖动中反复写配置文件）。</summary>
    private void ScheduleColumnSave()
    {
        if (_colSaveTimer == null)
        {
            _colSaveTimer = DispatcherQueue.CreateTimer();
            _colSaveTimer.Interval = TimeSpan.FromMilliseconds(500);
            _colSaveTimer.Tick += (_, _) =>
            {
                if (App.Settings is { } s)
                {
                    s.TimelineColumnWidths = TimelineColumns.ToPersisted(_colWidths);
                    s.Save();
                }
            };
        }
        _colSaveTimer.Stop();
        _colSaveTimer.Start();
    }

    /// <summary>双击表头拖拽热点：该列恢复默认宽（名称列在滚动状态下复位到下限；★态无可复位）。</summary>
    private void Grip_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (sender is not CommunityToolkit.WinUI.Controls.GridSplitter grip) return;
        var idx = Grid.GetColumn(grip);
        if (idx == TimelineColumns.NameColumn)
        {
            if (_nameIsFixed)
                ColName.Width = new GridLength(TimelineColumns.Minimums[TimelineColumns.NameColumn]); // 触发回调同步
            return;
        }
        _headerCols[idx].Width = new GridLength(TimelineColumns.Defaults[idx]); // 触发同步回调（钳制/行/落盘）
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

    /// <summary>双击行：打开文件所在位置。</summary>
    private async void EventList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        => await Vm.OpenLocationAsync(EventList.SelectedItem as ActivityEventItem);

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
