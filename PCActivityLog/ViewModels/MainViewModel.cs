using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using PCActivityLog.Data;
using PCActivityLog.Models;
using PCActivityLog.Services;
// System.Diagnostics（Process 类）里有同名的 ActivityEvent，这里固定用我们自己的模型
using ActivityEvent = PCActivityLog.Models.ActivityEvent;

namespace PCActivityLog.ViewModels;

/// <summary>筛选下拉框的选项（枚举 + 中文名）。</summary>
public record GroupOption(EventGroup Group, string Name)
{
    public override string ToString() => Name;
}

/// <summary>
/// 时间线页 ViewModel —— 筛选/搜索/分页/导出/打开位置/关联跳转。
/// 搜索防抖 400ms；新事件入库后若主窗口可见则最多每 10 秒自动刷新一次。
/// </summary>
public partial class MainViewModel : ObservableObject, IDisposable
{
    private const int PageSize = 500;

    private readonly Database _db;
    private readonly WriteQueue _queue;
    private readonly ExportService _exporter;

    /// <summary>搜索防抖定时器。</summary>
    private readonly DispatcherTimer _debounce;

    /// <summary>尾沿补刷定时器：节流窗口（5 秒）内到达的事件合并为一次延后刷新。</summary>
    private readonly DispatcherTimer _trailingRefresh;

    /// <summary>UI 线程调度器（构造时捕获）。后台线程回调里绝不能碰 WPF 对象，
    /// 一切涉及依赖属性的操作都经它编组到 UI 线程执行。</summary>
    private readonly Dispatcher _uiDispatcher;

    /// <summary>上次自动刷新时间（前沿节流用）。</summary>
    private DateTime _lastAutoRefresh = DateTime.MinValue;

    /// <summary>上次窗口显示/激活补刷时间（RefreshIfIdle 自身节流用）。</summary>
    private DateTime _lastIdleRefresh = DateTime.MinValue;

    [ObservableProperty] private GroupOption selectedGroup = new(EventGroup.AllNoBrowse, "全部");
    [ObservableProperty] private string searchText = "";
    [ObservableProperty] private DateTime? dateFrom;
    [ObservableProperty] private DateTime? dateTo;
    [ObservableProperty] private string statusText = "就绪";
    [ObservableProperty] private bool hasMore;

    public ObservableCollection<ActivityEventItem> Items { get; } = new();
    public ObservableCollection<GroupOption> Groups { get; } = new();

    /// <summary>统计页 VM（由 App 注入，XAML 的统计页直接绑定到它）。</summary>
    public StatsViewModel? Stats { get; set; }

    private int _loadedCount;

    public MainViewModel(Database db, WriteQueue queue, ExportService exporter)
    {
        _db = db;
        _queue = queue;
        _exporter = exporter;
        _uiDispatcher = Dispatcher.CurrentDispatcher; // 本构造函数固定在 UI 线程调用

        foreach (EventGroup g in Enum.GetValues(typeof(EventGroup)))
            Groups.Add(new GroupOption(g, g.ToDisplayName()));

        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _debounce.Tick += (_, _) => OnDebounceTick();

        _trailingRefresh = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _trailingRefresh.Tick += (_, _) =>
        {
            _trailingRefresh.Stop();
            if (!CanAutoRefresh()) return;
            _lastAutoRefresh = DateTime.Now;
            Refresh();
        };

        _queue.EventsCommitted += OnEventsCommitted;
    }

    /// <summary>搜索防抖到期：若在"全部"视图则先扩到"全部（含浏览）"再查。</summary>
    /// <remarks>浏览记录默认不展示，但用户主动搜索时应能搜到。</remarks>
    private void OnDebounceTick()
    {
        _debounce.Stop();
        EnsureBrowseSearchable();
        Refresh();
    }

    // ---------- 筛选与刷新 ----------

    partial void OnSelectedGroupChanged(GroupOption value) => Refresh();
    partial void OnDateFromChanged(DateTime? value) => Refresh();
    partial void OnDateToChanged(DateTime? value) => Refresh();
    partial void OnSearchTextChanged(string value) { _debounce.Stop(); _debounce.Start(); }

    /// <summary>搜索框有内容时自动切到"全部（含浏览）"，保证能搜到浏览记录。</summary>
    private void EnsureBrowseSearchable()
    {
        if (!string.IsNullOrWhiteSpace(SearchText) && SelectedGroup.Group == EventGroup.AllNoBrowse)
            SelectedGroup = Groups.Last(g => g.Group == EventGroup.All);
    }

    /// <summary>构造当前筛选条件。</summary>
    private Database.QueryFilter CurrentFilter(int offset) => new(
        SelectedGroup.Group, SearchText, DateFrom, DateTo, offset, PageSize);

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
            StatusText = $"共 {total} 条，已显示 {_loadedCount} 条"
                       + (HasMore ? "（可加载更多）" : "");
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
        catch (Exception ex)
        {
            DiagnosticsLog.Error("加载更多失败", ex);
        }
    }

    /// <summary>新事件入库回调：前沿+尾沿节流刷新。
    /// 修复点：旧实现节流窗口内的事件被直接丢弃且不补刷，导致"要切筛选才能看到新记录"。</summary>
    /// <summary>新事件入库回调：前沿+尾沿节流刷新。
    /// 根因修复：本方法运行在写队列的后台线程，旧代码读 MainWindow/IsVisible 等
    /// WPF 属性触发跨线程异常且被静默吞掉——自动刷新因此从未生效过。
    /// 现在后台线程只做调度（用构造时捕获的 Dispatcher），判断全部在 UI 线程做。</summary>
    private void OnEventsCommitted(object? sender, IReadOnlyList<ActivityEvent> events)
    {
        try
        {
            _uiDispatcher.BeginInvoke(ArmAutoRefresh);
        }
        catch (Exception ex) { DiagnosticsLog.Error("自动刷新调度失败", ex); }
    }

    /// <summary>是否允许自动刷新（必须在 UI 线程调用：读 IsVisible 与 SearchText）。</summary>
    private bool CanAutoRefresh()
        => Application.Current?.MainWindow is { IsVisible: true } && string.IsNullOrEmpty(SearchText);

    /// <summary>前沿+尾沿节流（5 秒窗口，UI 线程执行）：
    /// 距上次刷新足够久 → 立即刷新（前沿）；
    /// 窗口期内 → 把到达的事件合并为窗口结束时的一次延后刷新（尾沿），绝不丢。</summary>
    private void ArmAutoRefresh()
    {
        if (!CanAutoRefresh()) return;
        var since = DateTime.Now - _lastAutoRefresh;
        if (since >= TimeSpan.FromSeconds(5))
        {
            _lastAutoRefresh = DateTime.Now;
            _trailingRefresh.Stop();
            Refresh();
        }
        else
        {
            // 尾沿：重设到本窗口结束时刻；窗口内再多事件也只合并为这一次补刷
            _trailingRefresh.Stop();
            _trailingRefresh.Interval = TimeSpan.FromSeconds(5) - since;
            _trailingRefresh.Start();
        }
    }

    /// <summary>窗口重新显示/被激活时的补刷（2 秒自身节流，避免每次点击都查库）。</summary>
    public void RefreshIfIdle()
    {
        if (!CanAutoRefresh()) return;
        if (DateTime.Now - _lastIdleRefresh < TimeSpan.FromSeconds(2)) return;
        _lastIdleRefresh = DateTime.Now;
        _lastAutoRefresh = DateTime.Now;
        _trailingRefresh.Stop();
        Refresh();
    }

    // ---------- 行操作 ----------

    /// <summary>添加手动记录：弹出对话框，确认后入队。</summary>
    [RelayCommand]
    public void AddEvent()
    {
        var owner = Application.Current?.MainWindow;
        App.ShowAddEventDialog(owner ?? null!);
    }

    /// <summary>双击行：有关联下载 → 跳转；否则打开文件位置或网址。</summary>
    [RelayCommand]
    public void OpenOrJump(ActivityEventItem? item)
    {
        if (item is null) return;
        if (item.LinkedDownloadId is long id && TryJumpTo(id)) return;
        OpenLocation(item);
    }

    /// <summary>打开文件所在文件夹（选中该文件）或用浏览器打开网址。</summary>
    [RelayCommand]
    public void OpenLocation(ActivityEventItem? item)
    {
        if (item is null) return;
        try
        {
            if (!string.IsNullOrEmpty(item.Event.Url) && string.IsNullOrEmpty(item.Event.Path)
                && item.Event.Type == EventType.Browse)
            {
                Process.Start(new ProcessStartInfo(item.Event.Url) { UseShellExecute = true });
            }
            else if (!string.IsNullOrEmpty(item.Event.Path))
            {
                var path = item.Event.Path;
                if (File.Exists(path))
                    Process.Start("explorer.exe", $"/select,\"{path}\"");
                else if (Directory.Exists(path))
                    Process.Start("explorer.exe", $"\"{path}\"");
                else
                    MessageBox.Show("文件已不存在（可能已被删除或移动）。",
                        "电脑日志记录", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show("打开失败: " + ex.Message, "电脑日志记录",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>跳转到关联的下载事件行。</summary>
    [RelayCommand]
    public void JumpLinked(ActivityEventItem? item)
    {
        if (item?.LinkedDownloadId is long id) TryJumpTo(id);
    }

    /// <summary>尝试在列表中定位指定事件；不在当前页则按文件名搜索后定位。</summary>
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
        RequestSelect(idx);
        return true;
    }

    /// <summary>请求界面选中某行（事件方式通知 MainWindow，避免 VM 依赖控件）。</summary>
    public event Action<int>? SelectRequested;

    private void RequestSelect(int index) => SelectRequested?.Invoke(index);

    private int IndexOf(long eventId)
    {
        for (var i = 0; i < Items.Count; i++)
            if (Items[i].Id == eventId) return i;
        return -1;
    }

    /// <summary>编辑某行备注。</summary>
    [RelayCommand]
    public void EditNote(ActivityEventItem? item)
    {
        if (item is null) return;
        var newText = Views.NoteDialog.Prompt("编辑备注", item.Event.Note ?? "",
            "为这条记录添加备注（可在搜索框中搜到）：");
        if (newText is null) return; // 取消
        try
        {
            _db.UpdateNote(item.Event.Id, string.IsNullOrWhiteSpace(newText) ? null : newText.Trim());
            Refresh();
        }
        catch (Exception ex)
        {
            DiagnosticsLog.Error("更新备注失败", ex);
        }
    }

    // ---------- 导出 ----------

    [RelayCommand]
    public void ExportCsv() => Export(".csv", "CSV 文件|*.csv",
        f => _exporter.ExportCsv(CurrentFilter(0) with { Offset = 0, Limit = int.MaxValue }, f), "CSV");

    [RelayCommand]
    public void ExportJson() => Export(".json", "JSON 文件|*.json",
        f => _exporter.ExportJson(CurrentFilter(0) with { Offset = 0, Limit = int.MaxValue }, f), "JSON");

    /// <summary>导出的公共流程：选文件 → 后台导出 → 提示结果。</summary>
    private void Export(string defaultExt, string filter, Func<string, int> doExport, string kind)
    {
        var dlg = new SaveFileDialog
        {
            Filter = filter,
            DefaultExt = defaultExt,
            FileName = "电脑日志记录_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + defaultExt,
        };
        if (dlg.ShowDialog() != true) return;

        var path = dlg.FileName;
        StatusText = $"正在导出 {kind}…";
        Task.Run(() =>
        {
            try
            {
                var count = doExport(path);
                Application.Current?.Dispatcher.BeginInvoke(() =>
                {
                    StatusText = $"已导出 {count} 条到 {path}";
                    MessageBox.Show($"已导出 {count} 条事件。\n{path}", "电脑日志记录",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                });
            }
            catch (Exception ex)
            {
                DiagnosticsLog.Error($"导出{kind}失败", ex);
                Application.Current?.Dispatcher.BeginInvoke(() =>
                    MessageBox.Show("导出失败: " + ex.Message, "电脑日志记录",
                        MessageBoxButton.OK, MessageBoxImage.Error));
            }
        });
    }

    public void Dispose()
    {
        _debounce.Stop();
        _trailingRefresh.Stop();
        _queue.EventsCommitted -= OnEventsCommitted; // 配对解绑（规范第 3 条）
    }
}
