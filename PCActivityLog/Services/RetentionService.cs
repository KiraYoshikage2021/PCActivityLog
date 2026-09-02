using Microsoft.Data.Sqlite;
using PCActivityLog.Data;
using PCActivityLog.Services;

namespace PCActivityLog.Services;

/// <summary>
/// 数据保留服务 —— 按设置清理过期事件，控制数据库体积。
/// 浏览记录默认保留 90 天，其他事件默认永久（保留天数可在设置中调整，0 = 永久）。
/// 每 6 小时执行一次 + 程序启动 1 分钟后执行一次。
/// </summary>
public class RetentionService : IDisposable
{
    private readonly AppSettings _settings;
    private readonly Database _db;
    private System.Threading.Timer? _timer;

    public RetentionService(AppSettings settings, Database db)
    {
        _settings = settings;
        _db = db;
    }

    /// <summary>启动定时清理。</summary>
    public void Start()
    {
        Stop();
        _timer = new System.Threading.Timer(_ => SafeCleanup(), null,
            TimeSpan.FromMinutes(1), TimeSpan.FromHours(6));
    }

    /// <summary>停止。</summary>
    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }

    public void Dispose() => Stop();

    /// <summary>执行一轮清理（全程 try/catch）。</summary>
    private void SafeCleanup()
    {
        try
        {
            var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            var removed = 0;

            if (_settings.BrowserRetentionDays > 0)
                removed += DeleteOld("browse", now, _settings.BrowserRetentionDays);

            if (_settings.OtherRetentionDays > 0)
            {
                foreach (var type in new[] { "download", "file_delete", "file_rename",
                             "install", "update", "uninstall", "boot", "shutdown", "manual" })
                    removed += DeleteOld(type, now, _settings.OtherRetentionDays);
            }

            if (removed > 0)
            {
                CheckpointWal();
                DiagnosticsLog.Info($"数据保留清理：删除 {removed} 条过期事件");
            }
        }
        catch (Exception ex)
        {
            DiagnosticsLog.Error("数据保留清理异常", ex);
        }
    }

    /// <summary>删除某类型超过保留期的事件，返回删除条数。</summary>
    private int DeleteOld(string type, string nowIso, int retentionDays)
    {
        using var conn = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _db.DbPath,
            DefaultTimeout = 30,
            Pooling = true,
        }.ToString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM events WHERE type=@t AND occurred_at < @cutoff";
        cmd.Parameters.AddWithValue("@t", type);
        cmd.Parameters.AddWithValue("@cutoff",
            DateTime.Now.AddDays(-retentionDays).ToString("yyyy-MM-dd HH:mm:ss"));
        return cmd.ExecuteNonQuery();
    }

    /// <summary>清理后收缩 WAL 文件，把空间还给操作系统。</summary>
    private void CheckpointWal()
    {
        try
        {
            using var conn = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = _db.DbPath,
                DefaultTimeout = 30,
                Pooling = true,
            }.ToString());
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
            cmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            DiagnosticsLog.Warn("WAL checkpoint 失败: " + ex.Message);
        }
    }
}
