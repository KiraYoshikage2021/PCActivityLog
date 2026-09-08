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

    [ObservableProperty] private GroupOption selectedGroup = new(EventGroup.AllNoBrowse, "全部");
    [ObservableProperty] private string searchText = "";
    [ObservableProperty] private DateTimeOffset? dateFrom;
    [ObservableProperty] private DateTimeOffset? dateTo;
    [ObservableProperty] private string statusText = "就绪";
    [ObservableProperty] private bool hasMore;
    [ObservableProperty] private ActivityEventItem? selectedItem;

    public ObservableCollection<ActivityEventItem> Items { get; } = new();
    public ObservableCollection<GroupOption> Groups { get; } = new();

    public TimelineViewModel(Database db, WriteQueue queue, ExportService exporter)
    {
        _db = db;
        _queue = queue;
        _exporter = exporter;
        _dispatcher = DispatcherQueue.GetForCurrentThread();

        foreach (EventGroup g in Enum.GetValues(typeof(EventGroup)))
            Groups.Add(new GroupOption(g, g.ToDisplayName()));

        _debounce = _dispatcher.CreateTimer();
        _debounce.Interval = TimeSpan.FromMilliseconds(400);
        _debounce.IsRepeating = false;
        _debounce.Tick += (_, _) => OnDebounceTick();

        _trailingRefresh = _dispatcher.CreateTimer();
        _trailingRefresh.Interval = TimeSpan.FromSeconds(5);
        _trailingRefresh.IsRepeating = false;
        _trailingRefresh.Tick += (_, _) =>
        {
            _trailingRefresh.Stop();
            if (!CanAutoRefresh()) return;
            _lastAutoRefresh = DateTime.Now;
            Refresh();
        };

        _queue.EventsCommitted += OnEventsCommitted;
    }

    // ---------- 筛选与刷新 ----------

    partial void OnSelectedGroupChanged(GroupOption value) => Refresh();
    partial void OnDateFromChanged(DateTimeOffset? value) => Refresh();
    partial void OnDateToChanged(DateTimeOffset? value) => Refresh();
    partial void OnSearchTextChanged(string value) { _debounce?.Stop(); _debounce?.Start(); }

    /// <summary>搜索防抖到期：若在"全部"视图则先扩到"全部（含浏览）"再查。</summary>
    private void OnDebounceTick()
    {
        _debounce?.Stop();
        if (!string.IsNullOrWhiteSpace(SearchText) && SelectedGroup.Group == EventGroup.AllNoBrowse)
            SelectedGroup = Groups.Last(g => g.Group == EventGroup.All);
        Refresh();
    }

    /// <summary>构造当前筛选条件。</summary>
    private Database.QueryFilter CurrentFilter(int offset) => new(
        SelectedGroup.Group, SearchText,
        DateFrom?.DateTime, DateTo?.DateTime, offset, PageSize);

    /// <summary>重新加载第一页。</summary>
    [RelayCommand]
    public void Refresh()
    {
        try
        {
            var filter = CurrentFilter(0);
            var total = _db.CountEvents(filter);
            var rows = _db.QueryEvents(filter);

            Items.Clear();
            foreach (var e in rows) Items.Add(new ActivityEventItem(e));
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

    /// <summary>加载下一页（追加）。</summary>
    [RelayCommand]
    public void LoadMore()
    {
        try
        {
            var rows = _db.QueryEvents(CurrentFilter(_loadedCount));
            foreach (var e in rows) Items.Add(new ActivityEventItem(e));
            _loadedCount += rows.Count;
            StatusText = $"已显示 {_loadedCount} 条（继续滚动可加载更多）";
            HasMore = rows.Count == PageSize;
        }
        catch (Exception ex) { DiagnosticsLog.Error("加载更多失败", ex); }
    }

    // ---------- 自动刷新（前沿+尾沿节流，逻辑同 WPF 版） ----------

    private void OnEventsCommitted(object? sender, IReadOnlyList<ActivityEvent> events)
    {
        try { _dispatcher.TryEnqueue(ArmAutoRefresh); }
        catch (Exception ex) { DiagnosticsLog.Error("自动刷新调度失败", ex); }
    }

    /// <summary>是否允许自动刷新（须在 UI 线程调用）。</summary>
    private bool CanAutoRefresh()
        => App.MainWindowInstance != null && string.IsNullOrEmpty(SearchText);

    /// <summary>前沿+尾沿节流（5 秒窗口）：窗口期内到达的事件合并为一次延后刷新，绝不丢。</summary>
    private void ArmAutoRefresh()
    {
        if (!CanAutoRefresh()) return;
        var since = DateTime.Now - _lastAutoRefresh;
        if (since >= TimeSpan.FromSeconds(5))
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
                _trailingRefresh.Interval = TimeSpan.FromSeconds(5) - since;
                _trailingRefresh.Start();
            }
        }
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
            if (!string.IsNullOrEmpty(item.Event.Url) && string.IsNullOrEmpty(item.Event.Path)
                && item.Event.Type == EventType.Browse)
            {
                await Windows.System.Launcher.LaunchUriAsync(new Uri(item.Event.Url));
            }
            else if (!string.IsNullOrEmpty(item.Event.Path))
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
