using System.Diagnostics.Eventing.Reader;
using System.Text.RegularExpressions;
using PCActivityLog.Data;
using PCActivityLog.Models;
using PCActivityLog.Services;

namespace PCActivityLog.Watchers;

/// <summary>
/// 系统监视模块 —— 记录开机/关机/重启/睡眠/唤醒全形态。
///
/// 事件来源（系统事件日志）：
///   6005 = 事件日志服务启动（真开机）
///   6006 = 事件日志服务停止（真关机）
///   6008 = 上次关机是意外的（断电/死机）
///   1074 = 用户/进程发起的关机或重启（解析消息区分"关闭电源"/"重启"）
///   42   = 系统进入睡眠/休眠（"快速启动"的关机实际走这条）
///   107  = 从睡眠/休眠唤醒
///
/// 重要背景：Win11 默认开启"快速启动"（HiberbootEnabled=1）时，
/// 用户点"关机"会被执行成混合休眠——只产生 1074/187/42，不产生 6006/6005。
/// 因此仅订阅 6005/6006/6008 会漏掉绝大多数"关机"，必须同时读 1074/42/107。
///
/// 回填策略（修复了两个缺陷）：
///   1. EventLogReader 默认返回"最旧优先"，旧代码用 break 会在游标非空时第一条就中断
///      → 改为 ReverseDirection=true（最新优先），遇到 t &lt;= 游标才停
///   2. 游标存到毫秒精度，避免秒级截断把边界事件重复或漏记
/// </summary>
public class SystemEventWatcher : IWatcherModule
{
    public string Id => "system";
    public string DisplayName => "系统监视";

    private const string CursorKey = "sysevent_cursor";

    /// <summary>订阅的事件 ID：真开关机 + 意外关机 + 用户发起 + 睡眠/唤醒。</summary>
    private static readonly string Xpath =
        "*[System[(EventID=6005 or EventID=6006 or EventID=6008 or EventID=1074 or EventID=42 or EventID=107)]]";

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
        Backfill();      // 先回填停机期间的事件
        SubscribeLive(); // 再订阅实时事件
        IsRunning = true;
        DiagnosticsLog.Info("系统监视已启动（含开关机/重启/睡眠/唤醒）");
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

    /// <summary>
    /// 回填：反向（最新优先）读取游标之后的全部相关事件。
    /// 反向读取 + 遇到旧于游标即停，既不会漏新事件，也不会全量扫描日志。
    /// </summary>
    private void Backfill()
    {
        try
        {
            var cursor = _db.GetState(CursorKey);
            var cursorTime = DateTime.TryParse(cursor, out var ct) ? ct : (DateTime?)null;

            var collected = new List<(DateTime time, int id, string? message)>();
            var query = new EventLogQuery("System", PathType.LogName, Xpath)
            {
                ReverseDirection = true, // 最新优先：保证"从新往旧找，遇到游标就停"语义正确
            };
            using (var reader = new EventLogReader(query))
            {
                while (reader.ReadEvent() is { } rec)
                {
                    using (rec) // EventRecord 实现 IDisposable，必须释放非托管句柄
                    {
                        if (rec.TimeCreated?.ToLocalTime() is not { } t) continue;
                        // 反向读取：一旦遇到不晚于游标的事件，后面的只会更旧，可以安全停止。
                        // 注意用秒精度比较：数据库 occurred_at 只存到秒，
                        // 若用毫秒比较，同一秒内毫秒更大的事件会被误判为新事件而重复入库。
                        if (cursorTime.HasValue && TruncateToSecond(t) <= TruncateToSecond(cursorTime.Value)) break;

                        collected.Add((t, rec.Id, SafeMessage(rec)));
                        if (collected.Count >= 500) break; // 防御性上限（正常远达不到）
                    }
                }
            }

            // 反向读取得到的是"新→旧"，入库前转成时间升序
            foreach (var (time, id, message) in collected.OrderBy(x => x.time))
            {
                var ev = ToEvent(time, id, message);
                if (ev != null) _sink.Dispatch(ev);
            }

            if (collected.Count > 0)
            {
                var last = collected.Max(x => x.time);
                // 游标用秒精度，与数据库 occurred_at 保持一致（避免毫秒边界导致重复）
                _db.SetState(CursorKey, last.ToString("yyyy-MM-dd HH:mm:ss"));
                DiagnosticsLog.Info($"系统监视回填 {collected.Count} 条开关机/睡眠事件");
            }
            else if (cursorTime is null)
            {
                // 首次运行无数据也写游标，避免下次全量扫描
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

    /// <summary>
    /// 实时事件回调（try/catch 包裹 + 推进游标）。
    /// 去重：同一时刻同一类型只记一次（EventLogWatcher 可能对同一事件多次回调）。
    /// </summary>
    private void OnEvent(object? sender, EventRecordWrittenEventArgs e)
    {
        try
        {
            var rec = e.EventRecord;
            var t = rec?.TimeCreated?.ToLocalTime();
            if (rec is null || t is null) return;

            // 实时去重：同一秒 + 同一事件 ID 只处理一次
            var dedupeKey = $"{rec.Id}@{TruncateToSecond(t.Value):yyyy-MM-dd HH:mm:ss}";
            lock (_liveDedupe)
            {
                if (_liveDedupe.Contains(dedupeKey)) return;
                _liveDedupe.Add(dedupeKey);
                // 限制去重表大小，防止长期运行无限增长
                if (_liveDedupe.Count > 500)
                {
                    var keep = _liveDedupe.OrderByDescending(k => k).Take(250).ToList();
                    _liveDedupe.Clear();
                    foreach (var k in keep) _liveDedupe.Add(k);
                }
            }

            var ev = ToEvent(t.Value, rec.Id, SafeMessage(rec));
            if (ev != null) _sink.Dispatch(ev);
            // 游标用秒精度，与数据库 occurred_at 一致
            _db.SetState(CursorKey, TruncateToSecond(t.Value).ToString("yyyy-MM-dd HH:mm:ss"));
        }
        catch (Exception ex)
        {
            DiagnosticsLog.Error("处理系统事件异常", ex);
        }
    }

    /// <summary>实时事件去重表（同一秒同一事件 ID 只记一次）。</summary>
    private readonly HashSet<string> _liveDedupe = new();

    /// <summary>截断到秒（数据库 occurred_at 的精度）。</summary>
    private static DateTime TruncateToSecond(DateTime t)
        => new(t.Year, t.Month, t.Day, t.Hour, t.Minute, t.Second);

    /// <summary>安全读取事件消息（缺消息 DLL 时会抛异常）。</summary>
    private static string? SafeMessage(EventRecord rec)
    {
        try { return rec.FormatDescription(); }
        catch { return null; }
    }

    /// <summary>1074 消息里识别操作类型：关闭电源 / 重启 / 其他。</summary>
    private static readonly Regex PowerActionRegex =
        new(@"(关闭电源|重新启动|重启|shutdown|restart|power off|reboot)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>42 消息里识别是睡眠还是休眠。</summary>
    private static readonly Regex SleepKindRegex =
        new(@"(休眠|Hibernate)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// 事件 ID + 消息 → ActivityEvent。无法识别的事件返回 null（忽略）。
    /// </summary>
    private static ActivityEvent? ToEvent(DateTime time, int eventId, string? message)
    {
        var msg = message ?? "";
        return eventId switch
        {
            // 真开机
            6005 => Make(time, EventType.Boot, "电脑开机", "boot"),
            // 真关机
            6006 => Make(time, EventType.Shutdown, "电脑关机", "shutdown"),
            // 意外关机（断电/死机）
            6008 => Make(time, EventType.Shutdown, "电脑意外关机", "unexpected"),

            // 用户/进程发起：1074 消息里写明是"关闭电源"还是"重新启动"
            1074 => PowerActionRegex.Match(msg) is { Success: true } m
                ? (m.Value.Contains('重') || m.Value.Contains("restart", StringComparison.OrdinalIgnoreCase)
                    || m.Value.Contains("reboot", StringComparison.OrdinalIgnoreCase)
                        ? Make(time, EventType.Restart, "电脑重启", "user")
                        : Make(time, EventType.Shutdown, "电脑关机", "user"))
                : Make(time, EventType.Shutdown, "电脑关机", "user"),

            // 进入睡眠/休眠（快速启动的"关机"实际走这条）
            42 => SleepKindRegex.IsMatch(msg)
                ? Make(time, EventType.Sleep, "电脑休眠", "hibernate")
                : Make(time, EventType.Sleep, "电脑睡眠", "sleep"),

            // 从睡眠/休眠唤醒
            107 => Make(time, EventType.Wake, "电脑唤醒", "wake"),

            _ => null,
        };
    }

    /// <summary>构造系统事件（统一 source=system + 子类标记）。</summary>
    private static ActivityEvent Make(DateTime time, EventType type, string name, string kind) => new()
    {
        Type = type,
        Name = name,
        Source = "system",
        OccurredAt = time,
        Extra = $"{{\"kind\":\"{kind}\"}}",
    };
}
