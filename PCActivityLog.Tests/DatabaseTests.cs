using Microsoft.Data.Sqlite;
using PCActivityLog.Data;
using PCActivityLog.Models;
using PCActivityLog.Services;
using Xunit;

namespace PCActivityLog.Tests;

/// <summary>
/// 数据层测试：UTC 时间存储约定、旧数据自动迁移、筛选查询。
/// 全部使用临时库文件，不触碰真实的 %LOCALAPPDATA% 数据。
/// </summary>
public class DatabaseTests
{
    private static Database NewDb(string dir) => new(Path.Combine(dir, "activity.db"));

    private static DateTime TrimSeconds(DateTime t) => new(t.Year, t.Month, t.Day, t.Hour, t.Minute, t.Second);

    /// <summary>模拟 WriteQueue 的写入路径（BindInsert + InsertSql），返回自增主键。</summary>
    private static long InsertRaw(Database db, ActivityEvent e)
    {
        using var conn = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = db.DbPath, DefaultTimeout = 30, Pooling = true,
        }.ToString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = Database.InsertSql;
        Database.BindInsert(cmd, e, withId: false);
        cmd.ExecuteNonQuery();
        using var idCmd = conn.CreateCommand();
        idCmd.CommandText = "SELECT last_insert_rowid()";
        return (long)idCmd.ExecuteScalar()!;
    }

    private static object? ExecScalar(Database db, string sql)
    {
        using var conn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = db.DbPath }.ToString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar();
    }

    private static Database.QueryFilter All(int limit = 100) =>
        new(EventGroup.All, null, null, null, 0, limit);

    // ---------- 时间存储 ----------

    [Fact]
    public void BindInsert_StoresUtcString_QueryReturnsLocalRoundTrip()
    {
        var dir = TestDir.Create();
        try
        {
            var db = NewDb(dir);
            db.Initialize();
            var at = new DateTime(2026, 6, 1, 15, 30, 0); // 本地时间（Unspecified = 本地语义）
            var e = new ActivityEvent
            {
                Type = EventType.Install, Name = "App", Version = "1.0",
                Path = @"C:\x\app.exe", Url = "https://example.com", Note = "备注", OccurredAt = at,
            };
            InsertRaw(db, e);

            // 库内原始字符串必须是 UTC（而非本地时间字符串）
            var raw = (string)ExecScalar(db, "SELECT occurred_at FROM events")!;
            Assert.Equal(at.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss"), raw);

            // 读回对象模型应是本地时间往返（Kind 不同但 ticks 相等）
            var rows = db.QueryEvents(All());
            var row = Assert.Single(rows);
            Assert.Equal(at, row.OccurredAt);
            Assert.Equal("App", row.Name);
            Assert.Equal(@"C:\x\app.exe", row.Path);
            Assert.Equal("https://example.com", row.Url);
            Assert.Equal("备注", row.Note);
        }
        finally { TestDir.Cleanup(dir); }
    }

    [Fact]
    public void Initialize_MigratesLegacyLocalRowsToUtc_Once()
    {
        var dir = TestDir.Create();
        try
        {
            var db = NewDb(dir);
            db.Initialize(); // 首次初始化（空库，写入标记）

            // 模拟旧版本数据库：用原始 SQL 直插"本地时间"格式的行（不能走 BindInsert——
            // 它会把时间转成 UTC，就不是旧格式了），再清掉迁移标记
            ExecScalar(db, $"INSERT INTO events(type,name,occurred_at) VALUES('download','legacy.zip','2026-01-01 12:00:00')");
            ExecScalar(db, $"DELETE FROM {Database.KvTable} WHERE key='time_store_utc'");

            db.Initialize(); // 第二次初始化触发迁移

            Assert.Equal("1", db.GetState("time_store_utc")); // 标记已写回
            var row = Assert.Single(db.QueryEvents(All()));
            Assert.Equal(new DateTime(2026, 1, 1, 12, 0, 0), row.OccurredAt); // 本地时间往返

            // 再次初始化不应重复平移（幂等）
            db.Initialize();
            Assert.Equal(new DateTime(2026, 1, 1, 12, 0, 0), Assert.Single(db.QueryEvents(All())).OccurredAt);
        }
        finally { TestDir.Cleanup(dir); }
    }

    [Fact]
    public void DateRangeFilter_UsesLocalCalendarDay()
    {
        var dir = TestDir.Create();
        try
        {
            var db = NewDb(dir);
            db.Initialize();
            var today = DateTime.Today.AddHours(12);
            InsertRaw(db, new ActivityEvent { Type = EventType.Download, Name = "today.zip", OccurredAt = today });
            InsertRaw(db, new ActivityEvent { Type = EventType.Manual, Name = "old.txt", OccurredAt = today.AddDays(-10) });

            // 筛"今天"（本地日历日）只命中一条；From/To 传本地日期，库内 UTC 由边界统一转换
            var f = new Database.QueryFilter(EventGroup.All, null, DateTime.Today, DateTime.Today, 0, 10);
            Assert.Single(db.QueryEvents(f));
            Assert.Equal(1, db.CountEvents(f));
        }
        finally { TestDir.Cleanup(dir); }
    }

    // ---------- 筛选与查询 ----------

    [Fact]
    public void GroupAndSearch_Filters()
    {
        var dir = TestDir.Create();
        try
        {
            var db = NewDb(dir);
            db.Initialize();
            var now = TrimSeconds(DateTime.Now);
            InsertRaw(db, new ActivityEvent { Type = EventType.Install, Name = "Firefox", Note = "浏览器", OccurredAt = now });
            InsertRaw(db, new ActivityEvent { Type = EventType.Download, Name = "setup.zip", OccurredAt = now });

            Assert.Single(db.QueryEvents(new Database.QueryFilter(EventGroup.App, null, null, null, 0, 10)));

            // 关键词搜索命中备注
            var byNote = db.QueryEvents(new Database.QueryFilter(EventGroup.All, "浏览器", null, null, 0, 10));
            Assert.Equal("Firefox", Assert.Single(byNote).Name);

            Assert.Equal(2, db.CountEvents(All()));
        }
        finally { TestDir.Cleanup(dir); }
    }

    [Fact]
    public void UpdateNote_UpdateExtra_GetEvent_Work()
    {
        var dir = TestDir.Create();
        try
        {
            var db = NewDb(dir);
            db.Initialize();
            var id = InsertRaw(db, new ActivityEvent { Type = EventType.Manual, Name = "n1", OccurredAt = TrimSeconds(DateTime.Now) });

            db.UpdateNote(id, "我的备注");
            db.UpdateExtra(id, "{\"linkedDownloadId\": 7}");
            var e = db.GetEvent(id);
            Assert.NotNull(e);
            Assert.Equal("我的备注", e!.Note);
            Assert.Equal(7, e.GetExtraLong("linkedDownloadId"));
            Assert.Null(db.GetEvent(99999));
        }
        finally { TestDir.Cleanup(dir); }
    }

    [Fact]
    public void HasInstallEvent_DetectsSameNameAndVersion()
    {
        var dir = TestDir.Create();
        try
        {
            var db = NewDb(dir);
            db.Initialize();
            InsertRaw(db, new ActivityEvent { Type = EventType.Install, Name = "AppX", Version = "2.5", OccurredAt = TrimSeconds(DateTime.Now) });

            Assert.True(db.HasInstallEvent("AppX", "2.5"));
            Assert.False(db.HasInstallEvent("AppX", "3.0"));
            Assert.False(db.HasInstallEvent("AppY", "2.5"));
        }
        finally { TestDir.Cleanup(dir); }
    }

    [Fact]
    public void QueryDownloadsSince_ReturnsOnlyDownloadType()
    {
        var dir = TestDir.Create();
        try
        {
            var db = NewDb(dir);
            db.Initialize();
            var now = TrimSeconds(DateTime.Now);
            InsertRaw(db, new ActivityEvent { Type = EventType.Download, Name = "a.zip", OccurredAt = now });
            InsertRaw(db, new ActivityEvent { Type = EventType.Install, Name = "App", OccurredAt = now });

            var rows = db.QueryDownloadsSince(now.AddMinutes(-1));
            Assert.Equal("a.zip", Assert.Single(rows).Name);
        }
        finally { TestDir.Cleanup(dir); }
    }

    // ---------- 写队列 ----------

    [Fact]
    public async Task WriteQueue_Enqueue_PersistsAndBackfillsId()
    {
        var dir = TestDir.Create();
        try
        {
            var db = NewDb(dir);
            db.Initialize();
            using var queue = new WriteQueue(db);

            var committed = new TaskCompletionSource();
            queue.EventsCommitted += (_, _) => committed.TrySetResult();

            var e = new ActivityEvent { Type = EventType.Download, Name = "q.zip", OccurredAt = TrimSeconds(DateTime.Now) };
            queue.Enqueue(e);
            await committed.Task.WaitAsync(TimeSpan.FromSeconds(10));
            queue.FlushAsync();

            var row = Assert.Single(db.QueryEvents(All()));
            Assert.Equal("q.zip", row.Name);
            Assert.NotEqual(0, row.Id);       // 自增主键已回填（事件关联器依赖）
            Assert.Equal(row.Id, e.Id);
        }
        finally { TestDir.Cleanup(dir); }
    }
}
