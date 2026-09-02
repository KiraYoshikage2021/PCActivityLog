using System.IO;
using Microsoft.Data.Sqlite;
using PCActivityLog.Data;
using PCActivityLog.Models;
using PCActivityLog.Services;

namespace PCActivityLog.Watchers;

/// <summary>
/// 浏览器记录模块 —— 定时增量导入 Chrome / Edge / Firefox 的浏览历史。
///
/// 关键设计：
///   1. 浏览器运行时会锁定历史库 → 每轮把库文件（连同 -wal/-shm）复制到临时目录再打开，
///      复制带 3 次重试，彻底避开锁冲突；
///   2. 增量游标（每个浏览器每个 Profile 一个 max visit id，存数据库 kv_state 表），
///      每轮只导入 id 大于游标的访问记录；
///   3. 首次见到某 Profile：只初始化游标为当前最大 id，不导入历史存量
///      （行为可预期：从本软件运行开始记录）；
///   4. 每个子浏览器（Chrome/Edge/Firefox）可独立开关；
///   5. 临时副本用完即删，不留垃圾。
/// </summary>
public class BrowserHistoryWatcher : IWatcherModule
{
    public string Id => "browser";
    public string DisplayName => "浏览器记录";

    private readonly AppSettings _settings;
    private readonly IEventSink _sink;
    private readonly Database _db;
    private System.Threading.Timer? _pollTimer;

    /// <summary>单轮单 Profile 最多导入的访问数（防极端情况一轮拉爆内存）。</summary>
    private const int BatchLimit = 2000;

    /// <summary>临时目录：%TEMP%\PCActivityLog\</summary>
    private static readonly string TempDir = Path.Combine(Path.GetTempPath(), "PCActivityLog");

    public BrowserHistoryWatcher(AppSettings settings, IEventSink sink, Database db)
    {
        _settings = settings;
        _sink = sink;
        _db = db;
    }

    public bool IsRunning { get; private set; }

    public void Start()
    {
        Stop();
        Directory.CreateDirectory(TempDir);
        // 首轮延迟 20 秒再跑：开机时浏览器可能还没起来，错开启动高峰
        _pollTimer = new System.Threading.Timer(_ => SafePoll(), null,
            TimeSpan.FromSeconds(20), TimeSpan.FromMinutes(Math.Max(1, _settings.BrowserPollMinutes)));
        IsRunning = true;
        DiagnosticsLog.Info($"浏览器记录已启动，轮询间隔 {_settings.BrowserPollMinutes} 分钟");
    }

    public void Stop()
    {
        IsRunning = false;
        _pollTimer?.Dispose();
        _pollTimer = null;
    }

    public void Dispose()
    {
        Stop();
        try { if (Directory.Exists(TempDir)) Directory.Delete(TempDir, true); } catch { /* 忽略清理失败 */ }
    }

    /// <summary>一轮轮询：处理所有启用的浏览器 Profile。全程 try/catch。</summary>
    private void SafePoll()
    {
        try
        {
            if (_settings.BrowserChrome) ProcessChromium("chrome",
                Path.Combine(LocalAppData, @"Google\Chrome\User Data"));
            if (_settings.BrowserEdge) ProcessChromium("edge",
                Path.Combine(LocalAppData, @"Microsoft\Edge\User Data"));
            if (_settings.BrowserFirefox) ProcessFirefox();
        }
        catch (Exception ex)
        {
            DiagnosticsLog.Error("浏览器记录轮询异常", ex);
        }
    }

    private static string LocalAppData =>
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    // ================= Chrome / Edge（同为 Chromium 方案，历史库结构一致） =================

    /// <summary>处理一个 Chromium 系浏览器的所有 Profile。</summary>
    private void ProcessChromium(string browserId, string userDataDir)
    {
        if (!Directory.Exists(userDataDir)) return;
        foreach (var dir in Directory.GetDirectories(userDataDir))
        {
            var profileName = Path.GetFileName(dir);
            if (profileName is "System Profile" or "Guest Profile") continue; // 系统配置，跳过
            var historyPath = Path.Combine(dir, "History");
            if (!File.Exists(historyPath)) continue;
            try
            {
                ProcessChromiumProfile(browserId, profileName, historyPath);
            }
            catch (Exception ex)
            {
                DiagnosticsLog.Warn($"处理 {browserId}/{profileName} 失败: {ex.Message}");
            }
        }
    }

    /// <summary>复制 → 读增量 → 入库 → 更新游标 → 删临时文件。</summary>
    private void ProcessChromiumProfile(string browserId, string profile, string historyPath)
    {
        var cursorKey = $"bh:{browserId}:{profile}";
        var tempCopy = CopyToTemp(historyPath);
        if (tempCopy is null) return;
        try
        {
            using var conn = OpenReadOnly(tempCopy);
            using var cmd = conn.CreateCommand();

            cmd.CommandText = "SELECT IFNULL(MAX(id),0) FROM visits";
            var maxId = (long)cmd.ExecuteScalar()!;

            var cursorRaw = _db.GetState(cursorKey);
            if (cursorRaw is null)
            {
                // 首次：只设游标，不导入存量历史
                _db.SetState(cursorKey, maxId.ToString());
                DiagnosticsLog.Info($"浏览器游标初始化 {browserId}/{profile} maxId={maxId}");
                return;
            }
            var cursor = long.TryParse(cursorRaw, out var c) ? c : 0;

            cmd.CommandText = """
                SELECT v.id, u.url, IFNULL(u.title,''), v.visit_time
                FROM visits v JOIN urls u ON u.id = v.url
                WHERE v.id > @c ORDER BY v.id LIMIT @lim
                """;
            cmd.Parameters.AddWithValue("@c", cursor);
            cmd.Parameters.AddWithValue("@lim", BatchLimit);

            var events = new List<ActivityEvent>();
            long newCursor = cursor;
            using (var r = cmd.ExecuteReader())
            {
                while (r.Read())
                {
                    newCursor = Math.Max(newCursor, r.GetInt64(0));
                    if (TryMakeBrowseEvent(r.GetString(1), r.GetString(2),
                            ChromiumTime(r.GetInt64(3)), browserId, out var ev))
                        events.Add(ev);
                }
            }

            foreach (var ev in events) _sink.Dispatch(ev);
            if (newCursor > cursor) _db.SetState(cursorKey, newCursor.ToString());
            if (events.Count > 0)
                DiagnosticsLog.Info($"浏览器记录 {browserId}/{profile} 导入 {events.Count} 条");
        }
        finally
        {
            DeleteTemp(tempCopy);
        }
    }

    /// <summary>Chromium 时间戳（1601-01-01 起的微秒，UTC）→ 本地 DateTime。</summary>
    private static DateTime ChromiumTime(long microsecondsUtc)
        => new DateTime(1601, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddTicks(microsecondsUtc * 10).ToLocalTime();

    // ================= Firefox =================

    /// <summary>处理 Firefox 的所有 Profile（places.sqlite）。</summary>
    private void ProcessFirefox()
    {
        var profilesDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            @"Mozilla\Firefox\Profiles");
        if (!Directory.Exists(profilesDir)) return;

        foreach (var dir in Directory.GetDirectories(profilesDir))
        {
            var placesPath = Path.Combine(dir, "places.sqlite");
            if (!File.Exists(placesPath)) continue;
            var profile = Path.GetFileName(dir);
            try
            {
                var cursorKey = $"bh:firefox:{profile}";
                var tempCopy = CopyToTemp(placesPath);
                if (tempCopy is null) continue;
                try
                {
                    using var conn = OpenReadOnly(tempCopy);
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = "SELECT IFNULL(MAX(id),0) FROM moz_historyvisits";
                    var maxId = (long)cmd.ExecuteScalar()!;

                    var cursorRaw = _db.GetState(cursorKey);
                    if (cursorRaw is null)
                    {
                        _db.SetState(cursorKey, maxId.ToString());
                        DiagnosticsLog.Info($"浏览器游标初始化 firefox/{profile} maxId={maxId}");
                        continue;
                    }
                    var cursor = long.TryParse(cursorRaw, out var c) ? c : 0;

                    cmd.CommandText = """
                        SELECT v.id, p.url, IFNULL(p.title,''), v.visit_date
                        FROM moz_historyvisits v JOIN moz_places p ON p.id = v.place_id
                        WHERE v.id > @c ORDER BY v.id LIMIT @lim
                        """;
                    cmd.Parameters.AddWithValue("@c", cursor);
                    cmd.Parameters.AddWithValue("@lim", BatchLimit);

                    var events = new List<ActivityEvent>();
                    long newCursor = cursor;
                    using (var r = cmd.ExecuteReader())
                    {
                        while (r.Read())
                        {
                            newCursor = Math.Max(newCursor, r.GetInt64(0));
                            if (TryMakeBrowseEvent(r.GetString(1), r.GetString(2),
                                    FirefoxTime(r.GetInt64(3)), "firefox", out var ev))
                                events.Add(ev);
                        }
                    }

                    foreach (var ev in events) _sink.Dispatch(ev);
                    if (newCursor > cursor) _db.SetState(cursorKey, newCursor.ToString());
                    if (events.Count > 0)
                        DiagnosticsLog.Info($"浏览器记录 firefox/{profile} 导入 {events.Count} 条");
                }
                finally
                {
                    DeleteTemp(tempCopy);
                }
            }
            catch (Exception ex)
            {
                DiagnosticsLog.Warn($"处理 firefox/{profile} 失败: {ex.Message}");
            }
        }
    }

    /// <summary>Firefox 时间戳（1970-01-01 起的微秒，UTC）→ 本地 DateTime。</summary>
    private static DateTime FirefoxTime(long microsecondsUtc)
        => new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddTicks(microsecondsUtc * 10).ToLocalTime();

    // ================= 公共辅助 =================

    /// <summary>构造浏览事件；过滤非 http(s) 协议（chrome://、about: 等内部页面）。</summary>
    private static bool TryMakeBrowseEvent(string url, string title, DateTime time, string source,
        out ActivityEvent ev)
    {
        ev = new ActivityEvent();
        if (string.IsNullOrEmpty(url)) return false;
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return false;

        var name = title.Trim();
        if (string.IsNullOrEmpty(name))
        {
            // 无标题时用域名代替
            if (Uri.TryCreate(url, UriKind.Absolute, out var u))
                name = u.Host;
            else return false;
        }

        ev = new ActivityEvent
        {
            Type = EventType.Browse,
            Name = name.Length > 200 ? name[..200] : name, // 防超长标题
            Url = url.Length > 1000 ? url[..1000] : url,
            Source = source,
            OccurredAt = time,
        };
        return true;
    }

    /// <summary>以只读模式打开临时库副本。</summary>
    private static SqliteConnection OpenReadOnly(string path)
    {
        var conn = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false, // 临时文件不入池，确保句柄及时释放
            DefaultTimeout = 5,
        }.ToString());
        conn.Open();
        return conn;
    }

    /// <summary>
    /// 把历史库（连同 -wal / -shm）复制到临时目录。带 3 次重试（浏览器可能瞬时锁文件）。
    /// 返回临时主文件路径；失败返回 null。
    /// </summary>
    private static string? CopyToTemp(string dbPath)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (attempt > 0) Thread.Sleep(800);
            var guid = Guid.NewGuid().ToString("N");
            var tempMain = Path.Combine(TempDir, guid + ".db");
            try
            {
                File.Copy(dbPath, tempMain, true);
                // 同步 WAL 副本：不同步会读到旧数据（最近的访问在 -wal 里）
                var wal = dbPath + "-wal";
                if (File.Exists(wal)) File.Copy(wal, tempMain + "-wal", true);
                return tempMain;
            }
            catch (IOException)
            {
                try { File.Delete(tempMain); } catch { /* 清理失败忽略 */ }
                // 被锁 → 重试
            }
            catch (Exception ex)
            {
                DiagnosticsLog.Warn($"复制浏览器历史库失败 {dbPath}: {ex.Message}");
                try { File.Delete(tempMain); } catch { }
                return null;
            }
        }
        return null;
    }

    /// <summary>删除临时副本（含 -wal/-shm）。</summary>
    private static void DeleteTemp(string tempMain)
    {
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            try { File.Delete(tempMain + suffix); } catch { /* 删除失败忽略，下次轮询覆盖 */ }
        }
    }
}
