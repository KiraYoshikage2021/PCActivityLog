using Microsoft.Data.Sqlite;
using PCActivityLog.Services;

namespace PCActivityLog.Data;

/// <summary>
/// 统计聚合查询 —— 供统计页使用：汇总卡片 + 按月分类柱状图数据。
/// 全部用 SQL 聚合，不把明细拉进内存。
/// </summary>
public class StatsRepository
{
    private readonly Database _db;

    public StatsRepository(Database db) => _db = db;

    /// <summary>汇总卡片数据（本月/累计）。</summary>
    public record SummaryCards(
        int MonthDownloads, long MonthDownloadBytes, int MonthInstalls,
        int MonthBrowses, long TotalEvents);

    /// <summary>查汇总卡片。monthStart = 本月 1 号 0 点。</summary>
    public SummaryCards GetSummary(DateTime monthStart)
    {
        using var conn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _db.DbPath, Pooling = true }.ToString());
        conn.Open();

        long Count(SqliteCommand cmd, string type)
        {
            cmd.Parameters.Clear();
            cmd.CommandText = "SELECT COUNT(*) FROM events WHERE type=@t AND occurred_at>=@from";
            cmd.Parameters.AddWithValue("@t", type);
            cmd.Parameters.AddWithValue("@from", monthStart.ToString("yyyy-MM-dd HH:mm:ss"));
            return (long)cmd.ExecuteScalar()!;
        }

        using var cmd = conn.CreateCommand();
        var monthDownloads = Count(cmd, "download");
        var monthInstalls = Count(cmd, "install");
        var monthBrowses = Count(cmd, "browse");

        cmd.Parameters.Clear();
        cmd.CommandText = "SELECT IFNULL(SUM(size_bytes),0) FROM events WHERE type='download' AND occurred_at>=@from";
        cmd.Parameters.AddWithValue("@from", monthStart.ToString("yyyy-MM-dd HH:mm:ss"));
        var monthBytes = (long)cmd.ExecuteScalar()!;

        cmd.Parameters.Clear();
        cmd.CommandText = "SELECT COUNT(*) FROM events";
        var total = (long)cmd.ExecuteScalar()!;

        return new SummaryCards((int)monthDownloads, monthBytes, (int)monthInstalls, (int)monthBrowses, total);
    }

    /// <summary>按月分类统计的一行（月份 + 各类别数量）。</summary>
    public record MonthRow(string Month, int Downloads, int Apps, int Browses, int Others);

    /// <summary>查最近 N 个月的分类统计（含本月），按月份升序返回。</summary>
    public List<MonthRow> GetMonthly(int months)
    {
        var result = new List<MonthRow>();
        var from = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1).AddMonths(-(months - 1));

        using var conn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _db.DbPath, Pooling = true }.ToString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT substr(occurred_at,1,7) AS m,
                   SUM(type='download') AS d,
                   SUM(type IN ('install','update','uninstall')) AS a,
                   SUM(type='browse') AS b,
                   SUM(type NOT IN ('download','install','update','uninstall','browse')) AS o
            FROM events
            WHERE occurred_at >= @from
            GROUP BY m ORDER BY m
            """;
        cmd.Parameters.AddWithValue("@from", from.ToString("yyyy-MM-dd HH:mm:ss"));
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            result.Add(new MonthRow(
                r.GetString(0),
                ToInt(r, 1), ToInt(r, 2), ToInt(r, 3), ToInt(r, 4)));
        }
        return result;
    }

    private static int ToInt(SqliteDataReader r, int i) => r.IsDBNull(i) ? 0 : (int)r.GetInt64(i);
}
