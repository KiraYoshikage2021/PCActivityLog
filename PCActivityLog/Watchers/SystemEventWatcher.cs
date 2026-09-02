using System.Diagnostics.Eventing.Reader;
using PCActivityLog.Data;
using PCActivityLog.Models;
using PCActivityLog.Services;

namespace PCActivityLog.Watchers;

/// <summary>
/// 系统监视模块 —— 记录开机/关机。
/// 数据来源：系统事件日志的标准事件：
///   6005 = 事件日志服务已启动（≈开机）
///   6006 = 事件日志服务已停止（≈正常关机）
///   6008 = 上一次关机是意外的（断电/死机）
/// 本程序不常驻也能补齐：启动时把"上次处理到的时间"之后的这类事件回填入库
/// （游标存在数据库 kv_state 表），运行中再实时订阅新事件。
/// </summary>
public class SystemEventWatcher : IWatcherModule
{
    public string Id => "system";
    public string DisplayName => "系统监视";

    private const string CursorKey = "sysevent_cursor";
    private static readonly string Xpath =
        "*[System[(EventID=6005 or EventID=6006 or EventID=6008)]]";

    private readonly IEventSink _sink;
    private readonly Database _db;
    private EventLogWatcher? _watcher;

    public SystemEventWatcher(IEventSink sink, Database db)
    {
        _sink = sink;
        _db = db;
    }

    public bool IsRunning { get; private set; }

    public void Start()
    {
        Stop();
        Backfill();      // 先回填停机期间的开机/关机
        SubscribeLive(); // 再订阅实时事件
        IsRunning = true;
        DiagnosticsLog.Info("系统监视已启动");
    }

    public void Stop()
    {
        IsRunning = false;
        if (_watcher != null)
        {
            _watcher.EventRecordWritten -= OnEvent;
            _watcher.Enabled = false;
            _watcher.Dispose();
            _watcher = null;
        }
    }

    public void Dispose() => Stop();

    /// <summary>回填：读取游标之后的 6005/6006/6008 事件并入库（上限 1000 条防首跑过慢）。</summary>
    private void Backfill()
    {
        try
        {
            var cursor = _db.GetState(CursorKey);
            var cursorTime = DateTime.TryParse(cursor, out var ct) ? ct : (DateTime?)null;

            var collected = new List<(DateTime time, int id)>();
            using (var reader = new EventLogReader(new EventLogQuery("System", PathType.LogName, Xpath)))
            {
                while (reader.ReadEvent() is { } rec && collected.Count < 1000)
                {
                    if (rec.TimeCreated?.ToLocalTime() is not { } t) continue;
                    if (cursorTime.HasValue && t <= cursorTime) break; // 已处理过
                    collected.Add((t, rec.Id));
                }
            }

            // EventLogReader 返回顺序不保证，按时间升序入库
            foreach (var (time, id) in collected.OrderBy(x => x.time))
                _sink.Dispatch(ToEvent(time, id));

            if (collected.Count > 0)
            {
                var last = collected.Max(x => x.time);
                _db.SetState(CursorKey, last.ToString("yyyy-MM-dd HH:mm:ss"));
                DiagnosticsLog.Info($"系统监视回填 {collected.Count} 条开机/关机事件");
            }
            else if (cursorTime is null)
            {
                // 首次运行没有任何数据时也写下游标，避免下次全量扫描
                _db.SetState(CursorKey, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            }
        }
        catch (Exception ex)
        {
            DiagnosticsLog.Error("系统事件回填失败", ex);
        }
    }

    /// <summary>订阅实时事件。</summary>
    private void SubscribeLive()
    {
        try
        {
            _watcher = new EventLogWatcher(new EventLogQuery("System", PathType.LogName, Xpath));
            _watcher.EventRecordWritten += OnEvent;
            _watcher.Enabled = true;
        }
        catch (Exception ex)
        {
            DiagnosticsLog.Error("系统事件订阅启动失败", ex);
        }
    }

    /// <summary>实时事件回调（try/catch 包裹 + 推进游标）。</summary>
    private void OnEvent(object? sender, EventRecordWrittenEventArgs e)
    {
        try
        {
            var rec = e.EventRecord;
            var t = rec?.TimeCreated?.ToLocalTime();
            if (rec is null || t is null) return;

            _sink.Dispatch(ToEvent(t.Value, rec.Id));
            _db.SetState(CursorKey, t.Value.ToString("yyyy-MM-dd HH:mm:ss"));
        }
        catch (Exception ex)
        {
            DiagnosticsLog.Error("处理系统事件异常", ex);
        }
    }

    /// <summary>事件 ID → ActivityEvent。</summary>
    private static ActivityEvent ToEvent(DateTime time, int eventId) => new()
    {
        Type = eventId == 6005 ? EventType.Boot : EventType.Shutdown,
        Name = eventId switch
        {
            6005 => "电脑开机",
            6006 => "电脑关机",
            _ => "电脑意外关机",
        },
        Source = "system",
        OccurredAt = time,
        Extra = eventId == 6008 ? "{\"unexpected\":true}" : null,
    };
}
