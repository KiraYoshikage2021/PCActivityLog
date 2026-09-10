using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PCActivityLog.Data;
using PCActivityLog.Models;
using PCActivityLog.Services;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
// System.Diagnostics（Process 类）里有同名的 ActivityEvent，固定用我们自己的模型
using ActivityEvent = PCActivityLog.Models.ActivityEvent;

namespace PCActivityLog.ViewModels;

/// <summary>筛选下拉框的选项（枚举 + 中文名）。</summary>
public record GroupOption(EventGroup Group, string Name)
{
    public override string ToString() => Name;
}

/// <summary>时间范围快捷选项（Days = 往前天数；0 表示不限）。</summary>
public record DateRangeOption(int Days, string Name)
{
    public override string ToString() => Name;
}

/// <summary>
/// 时间线页 ViewModel（WinUI 版）—— 筛选/搜索/分页/导出/打开位置/关联跳转。
/// 与 WPF 版的差异：
///   · DispatcherTimer → DispatcherQueueTimer
///   · MessageBox → ContentDialog（由页面提供委托）
///   · SaveFileDialog → FileSavePicker
///   · Clipboard → Windows.ApplicationModel.DataTransfer
/// 业务逻辑（前沿+尾沿节流刷新、分页、搜索防抖）与原版保持一致。
/// </summary>
public partial class TimelineViewModel : ObservableObject, IDisposable
{
    private const int PageSize = 500;

    private readonly Database _db;
    private readonly WriteQueue _queue;
    private readonly ExportService _exporter;
    private readonly DispatcherQueue _dispatcher;

    private DispatcherQueueTimer? _debounce;
    private DispatcherQueueTimer? _trailingRefresh;
    private DateTime _lastAutoRefresh = DateTime.MinValue;
    private DateTime _lastIdleRefresh = DateTime.MinValue;
    private int _loadedCount;

    /// <summary>由页面注入：弹提示对话框（替代 MessageBox）。</summary>
    public Func<string, string, Task>? ShowMessage { get; set; }

    /// <summary>由页面注入：跳转/选中某行（滚动到目标）。</summary>
    public Action<int>? SelectRequested { get; set; }

    [ObservableProperty] private GroupOption selectedGroup = new(EventGroup.All, "全部");
    [ObservableProperty] private string searchText = "";
    [ObservableProperty] private DateTimeOffset? dateFrom;
    [ObservableProperty] private DateTimeOffset? dateTo;
    [ObservableProperty] private DateRangeOption selectedRange;
    [ObservableProperty] private string statusText = "就绪";
    [ObservableProperty] private bool hasMore;
    [ObservableProperty] private ActivityEventItem? selectedItem;

    /// <summary>列表数据（批量替换集合：刷新时只发一次 Reset，避免几百次逐条通知卡顿）。</summary>
    public BulkObservableCollection<ActivityEventItem> Items { get; } = new();
    public ObservableCollection<GroupOption> Groups { get; } = new();
    public ObservableCollection<DateRangeOption> DateRanges { get; } = new();

    /// <summary>最近一次用户滚动/交互时间（自动刷新避开用户正在滚动的时刻）。</summary>
    private DateTime _lastUserActivity = DateTime.MinValue;

    public TimelineViewModel(Database db, WriteQueue queue, ExportService exporter)
    {
        _db = db;
        _queue = queue;
        _exporter = exporter;
        _dispatcher = DispatcherQueue.GetForCurrentThread();

        foreach (EventGroup g in Enum.GetValues(typeof(EventGroup)))
            Groups.Add(new GroupOption(g, g.ToDisplayName()));

        // 时间范围快捷选项；默认"最近 3 天"（避免一次加载上千条历史拖慢界面）
        DateRanges.Add(new DateRangeOption(0, "今天"));
        DateRanges.Add(new DateRangeOption(3, "最近 3 天"));
        DateRanges.Add(new DateRangeOption(7, "最近 7 天"));
        DateRanges.Add(new DateRangeOption(30, "最近 30 天"));
        DateRanges.Add(new DateRangeOption(90, "最近 90 天"));
        DateRanges.Add(new DateRangeOption(-1, "全部"));
        SelectedRange = DateRanges.First(r => r.Days == 3);
        ApplyRange(SelectedRange); // 初始化 dateFrom/dateTo

        _debounce = _dispatcher.CreateTimer();
        _debounce.Interval = TimeSpan.FromMilliseconds(400);
        _debounce.IsRepeating = false;
        _debounce.Tick += (_, _) => OnDebounceTick();

        _trailingRefresh = _dispatcher.CreateTimer();
        _trailingRefresh.Interval = TimeSpan.FromSeconds(5);
        _trailingRefresh.IsRepeating = false;
        _trailingRefresh.Tick += (_, _) => OnTrailingTick();

        _queue.EventsCommitted += OnEventsCommitted;
    }

    // ---------- 筛选与刷新 ----------

    partial void OnSelectedGroupChanged(GroupOption value) => Refresh();
    partial void OnDateFromChanged(DateTimeOffset? value) => Refresh();
    partial void OnDateToChanged(DateTimeOffset? value) => Refresh();
    partial void OnSearchTextChanged(string value) { _debounce?.Stop(); _debounce?.Start(); }

    /// <summary>时间范围切换：按选项设置 dateFrom/dateTo 并刷新。</summary>
    partial void OnSelectedRangeChanged(DateRangeOption value)
    {
        if (value is null) return;
        ApplyRange(value);
        Refresh();
    }

    /// <summary>
    /// 把时间范围选项换算成日期区间。
    /// Days = 0：仅今天；Days = N（&gt;0）：含今天在内共 N 天；Days &lt; 0：不限。
    /// </summary>
    private void ApplyRange(DateRangeOption range)
    {
        if (range.Days < 0)
        {
            DateFrom = null;
            DateTo = null;
            return;
        }
        var today = DateTime.Today;
        // Days=0（今天）起点即今天；Days=3 起点为 2 天前（含今天共 3 天）。
        // 注意：不能直接写 -(Days-1)，否则 Days=0 时起点会变成明天，导致查不到任何数据。
        int backDays = range.Days <= 1 ? 0 : range.Days - 1;
        DateFrom = new DateTimeOffset(today.AddDays(-backDays));
        DateTo = new DateTimeOffset(today);
    }

    /// <summary>搜索防抖到期后执行查询。</summary>
    private void OnDebounceTick()
    {
        _debounce?.Stop();
        Refresh();
    }

    /// <summary>构造当前筛选条件。</summary>
    private Database.QueryFilter CurrentFilter(int offset) => new(
        SelectedGroup.Group, SearchText,
        DateFrom?.DateTime, DateTo?.DateTime, offset, PageSize);

    /// <summary>重新加载第一页。结果集无变化时跳过重建（避免无谓的 UI 抖动）。</summary>
    [RelayCommand]
    public void Refresh()
    {
        try
        {
            var filter = CurrentFilter(0);
            var total = _db.CountEvents(filter);
            var rows = _db.QueryEvents(filter);
            var newestId = rows.Count > 0 ? rows[0].Id : 0;

            // 变更签名（总条数 + 最新事件 Id）与上次相同 → 数据没变，跳过集合重建。
            // 注意不能用 Items.Count 对比 total —— 用户点过"加载更多"后两者恒不等。
            if (_loadedCount > 0 && total == _lastTotal && newestId == _lastNewestId)
            {
                StatusText = $"共 {total} 条，已显示 {_loadedCount} 条" + (HasMore ? "（可加载更多）" : "");
                return;
            }
            _lastTotal = total;
            _lastNewestId = newestId;

            // 批量替换：整体只发一次 Reset 通知（Clear+逐条 Add 会触发几百次布局）
            var items = new List<ActivityEventItem>(rows.Count);
            foreach (var e in rows) items.Add(new ActivityEventItem(e));
            Items.ReplaceRange(items);

            _loadedCount = rows.Count;
            HasMore = _loadedCount < total;
            StatusText = $"共 {total} 条，已显示 {_loadedCount} 条" + (HasMore ? "（可加载更多）" : "");
        }
        catch (Exception ex)
        {
            StatusText = "查询失败: " + ex.Message;
            DiagnosticsLog.Error("时间线查询失败", ex);
        }
    }

    /// <summary>上次查询的变更签名（总条数 + 最新事件 Id），用于跳过无变化的刷新。</summary>
    private long _lastTotal = -1;
    private long _lastNewestId = -1;

    /// <summary>加载下一页（批量追加）。</summary>
    [RelayCommand]
    public void LoadMore()
    {
        try
        {
            var rows = _db.QueryEvents(CurrentFilter(_loadedCount));
            var items = new List<ActivityEventItem>(rows.Count);
            foreach (var e in rows) items.Add(new ActivityEventItem(e));
            Items.AppendRange(items);
            _loadedCount += rows.Count;
            StatusText = $"已显示 {_loadedCount} 条（继续滚动可加载更多）";
            HasMore = rows.Count == PageSize;
        }
        catch (Exception ex) { DiagnosticsLog.Error("加载更多失败", ex); }
    }

    // ---------- 自动刷新（前沿+尾沿节流 + 用户交互避让） ----------

    private void OnEventsCommitted(object? sender, IReadOnlyList<ActivityEvent> events)
    {
        try { _dispatcher.TryEnqueue(ArmAutoRefresh); }
        catch (Exception ex) { DiagnosticsLog.Error("自动刷新调度失败", ex); }
    }

    /// <summary>
    /// 是否允许自动刷新（须在 UI 线程调用）。
    /// 用户 3 秒内滚动过列表时不刷新——重建集合会打断滚动（卡顿感的来源之一），
    /// 尾沿定时器会在滚动停止后自动补刷（见 <see cref="OnTrailingTick"/>）。
    /// </summary>
    private bool CanAutoRefresh()
        => App.MainWindowInstance != null
           && string.IsNullOrEmpty(SearchText)
           && DateTime.Now - _lastUserActivity > TimeSpan.FromSeconds(3);

    /// <summary>页面注入的用户交互通知（滚轮/按下）：自动刷新避让 3 秒。</summary>
    public void NotifyUserActive() => _lastUserActivity = DateTime.Now;

    /// <summary>前沿+尾沿节流（5 秒窗口）：窗口期内到达的事件合并为一次延后刷新，绝不丢。</summary>
    private void ArmAutoRefresh()
    {
        if (string.IsNullOrEmpty(SearchText) is false) return; // 搜索中不刷
        var since = DateTime.Now - _lastAutoRefresh;
        if (since >= TimeSpan.FromSeconds(5) && CanAutoRefresh())
        {
            _lastAutoRefresh = DateTime.Now;
            _trailingRefresh?.Stop();
            Refresh();
        }
        else
        {
            _trailingRefresh?.Stop();
            if (_trailingRefresh != null)
            {
                _trailingRefresh.Interval = TimeSpan.FromSeconds(5) - since > TimeSpan.Zero
                    ? TimeSpan.FromSeconds(5) - since
                    : TimeSpan.FromSeconds(1);
                _trailingRefresh.Start();
            }
        }
    }

    /// <summary>尾沿定时器触发：条件满足则刷新；用户还在滚动则 1 秒后重试（补刷，不丢事件）。</summary>
    private void OnTrailingTick()
    {
        _trailingRefresh?.Stop();
        if (!CanAutoRefresh())
        {
            // 用户正在滚动 → 1 秒后再试；搜索中则放弃（下次事件到达会重新布防）
            if (string.IsNullOrEmpty(SearchText) && _trailingRefresh != null)
            {
                _trailingRefresh.Interval = TimeSpan.FromSeconds(1);
                _trailingRefresh.Start();
            }
            return;
        }
        _lastAutoRefresh = DateTime.Now;
        Refresh();
    }

    /// <summary>窗口重新显示/被激活时的补刷（2 秒自身节流）。</summary>
    public void RefreshIfIdle()
    {
        if (!CanAutoRefresh()) return;
        if (DateTime.Now - _lastIdleRefresh < TimeSpan.FromSeconds(2)) return;
        _lastIdleRefresh = DateTime.Now;
        _lastAutoRefresh = DateTime.Now;
        _trailingRefresh?.Stop();
        Refresh();
    }

    // ---------- 行操作 ----------

    /// <summary>双击行：有关联下载 → 跳转；否则打开位置。</summary>
    [RelayCommand]
    public async Task OpenOrJumpAsync(ActivityEventItem? item)
    {
        if (item is null) return;
        if (item.LinkedDownloadId is long id && TryJumpTo(id)) return;
        await OpenLocationAsync(item);
    }

    /// <summary>打开文件所在文件夹（选中该文件）或用浏览器打开网址。</summary>
    [RelayCommand]
    public async Task OpenLocationAsync(ActivityEventItem? item)
    {
        if (item is null) return;
        try
        {
            if (!string.IsNullOrEmpty(item.Event.Path))
            {
                var path = item.Event.Path;
                if (File.Exists(path))
                    Process.Start("explorer.exe", $"/select,\"{path}\"");
                else if (Directory.Exists(path))
                    Process.Start("explorer.exe", $"\"{path}\"");
                else if (ShowMessage != null)
                    await ShowMessage("文件已不存在（可能已被删除或移动）。", "电脑日志记录");
            }
        }
        catch (Exception ex)
        {
            if (ShowMessage != null) await ShowMessage("打开失败: " + ex.Message, "电脑日志记录");
        }
    }

    /// <summary>跳转到关联的下载事件行。</summary>
    [RelayCommand]
    public void JumpLinked(ActivityEventItem? item)
    {
        if (item?.LinkedDownloadId is long id) TryJumpTo(id);
    }

    private bool TryJumpTo(long eventId)
    {
        var idx = IndexOf(eventId);
        if (idx < 0)
        {
            var target = _db.GetEvent(eventId);
            if (target == null) return false;
            SearchText = System.IO.Path.GetFileName(target.Path ?? target.Name);
            Refresh();
            idx = IndexOf(eventId);
            if (idx < 0) return false;
        }
        SelectRequested?.Invoke(idx);
        return true;
    }

    private int IndexOf(long eventId)
    {
        for (var i = 0; i < Items.Count; i++)
            if (Items[i].Id == eventId) return i;
        return -1;
    }

    /// <summary>复制某行名称到剪贴板。</summary>
    [RelayCommand]
    public void CopyName(ActivityEventItem? item)
    {
        if (item is null) return;
        try
        {
            var dp = new DataPackage();
            dp.SetText(item.Name);
            Clipboard.SetContent(dp);
        }
        catch (Exception ex) { DiagnosticsLog.Warn("复制失败: " + ex.Message); }
    }

    // ---------- 导出 ----------

    /// <summary>导出 CSV（FileSavePicker）。</summary>
    public async Task ExportCsvAsync()
    {
        var file = await PickSaveFileAsync("CSV 文件", ".csv");
        if (file == null) return;
        await RunExportAsync(() => _exporter.ExportCsv(CurrentFilter(0) with { Offset = 0, Limit = int.MaxValue }, file.Path), "CSV");
    }

    /// <summary>导出 JSON（FileSavePicker）。</summary>
    public async Task ExportJsonAsync()
    {
        var file = await PickSaveFileAsync("JSON 文件", ".json");
        if (file == null) return;
        await RunExportAsync(() => _exporter.ExportJson(CurrentFilter(0) with { Offset = 0, Limit = int.MaxValue }, file.Path), "JSON");
    }

    private async Task<StorageFile?> PickSaveFileAsync(string label, string ext)
    {
        try
        {
            var picker = new FileSavePicker();
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.FileTypeChoices.Add(label, new List<string> { ext });
            picker.SuggestedFileName = "电脑日志记录_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
            // WinUI3 非打包应用必须给 picker 关联窗口句柄
            if (App.MainWindowInstance != null)
                WinRT.Interop.InitializeWithWindow.Initialize(picker,
                    WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance));
            return await picker.PickSaveFileAsync();
        }
        catch (Exception ex)
        {
            DiagnosticsLog.Error("选择保存文件失败", ex);
            return null;
        }
    }

    private async Task RunExportAsync(Func<int> doExport, string kind)
    {
        StatusText = $"正在导出 {kind}…";
        try
        {
            var count = await Task.Run(doExport);
            StatusText = $"已导出 {count} 条";
            if (ShowMessage != null) await ShowMessage($"已导出 {count} 条事件。", "电脑日志记录");
        }
        catch (Exception ex)
        {
            DiagnosticsLog.Error($"导出{kind}失败", ex);
            if (ShowMessage != null) await ShowMessage("导出失败: " + ex.Message, "电脑日志记录");
        }
    }

    public void Dispose()
    {
        _debounce?.Stop();
        _trailingRefresh?.Stop();
        _queue.EventsCommitted -= OnEventsCommitted;
    }
}
