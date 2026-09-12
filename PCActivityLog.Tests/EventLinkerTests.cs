using Microsoft.Data.Sqlite;
using PCActivityLog.Data;
using PCActivityLog.Models;
using PCActivityLog.Services;
using Xunit;

namespace PCActivityLog.Tests;

public class EventLinkerTests
{
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

    [Fact]
    public void OnInstallEvent_LinksRecentMatchingDownload_BothWays()
    {
        var dir = TestDir.Create();
        try
        {
            var db = new Database(Path.Combine(dir, "activity.db"));
            db.Initialize();

            var dlId = InsertRaw(db, new ActivityEvent
            {
                Type = EventType.Download,
                Name = "Firefox Setup 128.exe",
                Path = @"C:\Users\me\Downloads\Firefox Setup 128.exe",
                OccurredAt = DateTime.Now.AddMinutes(-5),
            });
            var installId = InsertRaw(db, new ActivityEvent
            {
                Type = EventType.Install, Name = "Firefox", OccurredAt = DateTime.Now,
            });

            // 模拟 WatcherManager 的调用方式：库中已入库、带 Id 的安装事件
            var installEvent = db.GetEvent(installId)!;
            new EventLinker(db).OnInstallEvent(installEvent);

            // 双向关联：安装事件记住来源下载，下载事件记住安装事件
            Assert.Equal(dlId, installEvent.GetExtraLong("linkedDownloadId"));
            Assert.Equal(dlId, db.GetEvent(installId)!.GetExtraLong("linkedDownloadId"));
            Assert.Equal(installId, db.GetEvent(dlId)!.GetExtraLong("linkedInstallId"));
        }
        finally { TestDir.Cleanup(dir); }
    }

    [Fact]
    public void OnInstallEvent_NoMatch_LeavesExtraEmpty()
    {
        var dir = TestDir.Create();
        try
        {
            var db = new Database(Path.Combine(dir, "activity.db"));
            db.Initialize();
            InsertRaw(db, new ActivityEvent
            {
                Type = EventType.Download,
                Name = "7z2409-x64.exe",
                Path = @"C:\downloads\7z2409-x64.exe",
                OccurredAt = DateTime.Now.AddMinutes(-5),
            });

            var installId = InsertRaw(db, new ActivityEvent { Type = EventType.Install, Name = "TotallyUnrelated", OccurredAt = DateTime.Now });
            var installEvent = db.GetEvent(installId)!;
            new EventLinker(db).OnInstallEvent(installEvent);

            Assert.Null(db.GetEvent(installId)!.Extra);
        }
        finally { TestDir.Cleanup(dir); }
    }
}
