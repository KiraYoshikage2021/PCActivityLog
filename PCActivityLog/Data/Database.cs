using System.IO;
using Microsoft.Data.Sqlite;
using PCActivityLog.Models;
using PCActivityLog.Services;

namespace PCActivityLog.Data;

/// <summary>
/// 数据库访问层 —— 负责 SQLite 连接、建表、事件查询（分页/筛选/搜索）、备注更新。
/// 线程模型：读操作每次用短连接（连接池复用）；写操作全部走 <see cref="WriteQueue"/> 单写队列，
/// 保证同一时刻只有一个写入者，从根本上避免 SQLite 并发写冲突。
/// </summary>
public class Database
{
    /// <summary>数据库文件完整路径。</summary>
    public readonly string DbPath;

    /// <summary>浏览器游标、系统事件游标等内部状态的存取键值表名。</summary>
    public const string KvTable = "kv_state";

    private readonly string _connString;

    public Database()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PCActivityLog");
        Directory.CreateDirectory(dir);
        DbPath = Path.Combine(dir, "activity.db");
        // Default Timeout：遇到锁时最多等待 30 秒（配合 WAL 几乎不会触发）
        _connString = new SqliteConnectionStringBuilder
        {
            DataSource = DbPath,
            DefaultTimeout = 30,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = true,
        }.ToString();
    }

    /// <summary>打开一个新连接（调用方负责 using 释放）。</summary>
    private SqliteConnection Open()
    {
        var conn = new SqliteConnection(_connString);
        conn.Open();
        return conn;
    }

    /// <summary>初始化：启用 WAL 模式 + 建表 + 建索引。程序启动时调用一次。</summary>
    public void Initialize()
    {
        using var conn = Open();
        using (var cmd = conn.CreateCommand())
        {
            // WAL 模式：写入不阻塞读取，且写入持久化到 db 文件后自动合并，是常驻程序的标准选择
            cmd.CommandText = "PRAGMA journal_mode=WAL;";
            cmd.ExecuteScalar();
        }
        using var init = conn.CreateCommand();
        init.CommandText = $"""
            CREATE TABLE IF NOT EXISTS events (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                type        TEXT    NOT NULL,              -- download / install / browse ...
                name        TEXT    NOT NULL,              -- 文件名/软件名/网页标题
                path        TEXT,                          -- 相关路径（可空）
                size_bytes  INTEGER,                       -- 文件大小（下载事件）
                url         TEXT,                          -- 下载来源网址 / 网页地址
                source      TEXT,                          -- msi/registry/chrome/edge/firefox/manual/system/file
                version     TEXT,                          -- 软件版本
                old_version TEXT,                          -- 更新前的旧版本
                occurred_at TEXT    NOT NULL,              -- 事件时间 yyyy-MM-dd HH:mm:ss（本地时间）
                note        TEXT,                          -- 用户备注
                extra       TEXT                           -- 附加信息 JSON
            );
            CREATE INDEX IF NOT EXISTS idx_events_time ON events(occurred_at DESC, id DESC);
            CREATE INDEX IF NOT EXISTS idx_events_type ON events(type);
            CREATE TABLE IF NOT EXISTS {KvTable} (
                key   TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );
            """;
        init.ExecuteNonQuery();
        DiagnosticsLog.Info($"数据库初始化完成: {DbPath}");
    }

    // ================= 查询 =================

    /// <summary>时间线查询参数：筛选 + 搜索 + 日期范围 + 分页。</summary>
    public record QueryFilter(EventGroup Group, string? Search, DateTime? From, DateTime? To, int Offset, int Limit);

    /// <summary>按筛选条件查询事件列表（时间倒序）。</summary>
    public List<ActivityEvent> QueryEvents(QueryFilter f)
    {
        var list = new List<ActivityEvent>();
        using var conn = Open();
        using var cmd = conn.CreateCommand();

        var where = new List<string>();
        var types = f.Group.ToDbTypeList();
        if (types.Length > 0)
        {
            // 类型 IN 条件（参数化防注入）
            var names = types.Select((_, i) => "@t" + i).ToArray();
            where.Add($"type IN ({string.Join(',', names)})");
            for (int i = 0; i < types.Length; i++)
                cmd.Parameters.AddWithValue("@t" + i, types[i]);
        }
        if (!string.IsNullOrWhiteSpace(f.Search))
        {
            where.Add("(name LIKE @q OR note LIKE @q OR path LIKE @q OR url LIKE @q)");
            cmd.Parameters.AddWithValue("@q", "%" + f.Search.Trim() + "%");
        }
        if (f.From.HasValue) where.Add("occurred_at >= @from");
        if (f.From.HasValue) cmd.Parameters.AddWithValue("@from", f.From.Value.ToString("yyyy-MM-dd HH:mm:ss"));
        if (f.To.HasValue) where.Add("occurred_at < @to");
        if (f.To.HasValue) cmd.Parameters.AddWithValue("@to", f.To.Value.AddDays(1).ToString("yyyy-MM-dd HH:mm:ss"));

        cmd.CommandText = $"""
            SELECT id, type, name, path, size_bytes, url, source, version, old_version, occurred_at, note, extra
            FROM events {(where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : "")}
            ORDER BY occurred_at DESC, id DESC
            LIMIT @limit OFFSET @offset
            """;
        cmd.Parameters.AddWithValue("@limit", f.Limit);
        cmd.Parameters.AddWithValue("@offset", f.Offset);

        using var reader = cmd.ExecuteReader();
        while (reader.Read()) list.Add(ReadEvent(reader));
        return list;
    }

    /// <summary>按筛选条件统计总条数（用于"加载更多"判断与状态栏）。</summary>
    public long CountEvents(QueryFilter f)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        var where = new List<string>();
        var types = f.Group.ToDbTypeList();
        if (types.Length > 0)
        {
            var names = types.Select((_, i) => "@t" + i).ToArray();
            where.Add($"type IN ({string.Join(',', names)})");
            for (int i = 0; i < types.Length; i++)
                cmd.Parameters.AddWithValue("@t" + i, types[i]);
        }
        if (!string.IsNullOrWhiteSpace(f.Search))
        {
            where.Add("(name LIKE @q OR note LIKE @q OR path LIKE @q OR url LIKE @q)");
            cmd.Parameters.AddWithValue("@q", "%" + f.Search.Trim() + "%");
        }
        if (f.From.HasValue) where.Add("occurred_at >= @from");
        if (f.From.HasValue) cmd.Parameters.AddWithValue("@from", f.From.Value.ToString("yyyy-MM-dd HH:mm:ss"));
        if (f.To.HasValue) where.Add("occurred_at < @to");
        if (f.To.HasValue) cmd.Parameters.AddWithValue("@to", f.To.Value.AddDays(1).ToString("yyyy-MM-dd HH:mm:ss"));

        cmd.CommandText = $"SELECT COUNT(*) FROM events {(where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : "")}";
        return (long)cmd.ExecuteScalar()!;
    }

    /// <summary>按主键取单条事件（用于关联跳转）。</summary>
    public ActivityEvent? GetEvent(long id)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, type, name, path, size_bytes, url, source, version, old_version, occurred_at, note, extra FROM events WHERE id=@id";
        cmd.Parameters.AddWithValue("@id", id);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? ReadEvent(reader) : null;
    }

    /// <summary>查询某时间之后、某类型的下载事件（供下载↔安装关联匹配）。</summary>
    public List<ActivityEvent> QueryDownloadsSince(DateTime since)
    {
        var list = new List<ActivityEvent>();
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, type, name, path, size_bytes, url, source, version, old_version, occurred_at, note, extra
            FROM events
            WHERE type='download' AND occurred_at >= @since
            ORDER BY id DESC LIMIT 200
            """;
        cmd.Parameters.AddWithValue("@since", since.ToString("yyyy-MM-dd HH:mm:ss"));
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) list.Add(ReadEvent(reader));
        return list;
    }

    /// <summary>更新某条事件的备注。</summary>
    public void UpdateNote(long id, string? note)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE events SET note=@note WHERE id=@id";
        cmd.Parameters.AddWithValue("@note", (object?)note ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
    }

    /// <summary>更新某条事件的 extra JSON（供事件关联器回写）。</summary>
    public void UpdateExtra(long id, string? extra)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE events SET extra=@extra WHERE id=@id";
        cmd.Parameters.AddWithValue("@extra", (object?)extra ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
    }

    /// <summary>查询某时间之后、某类型的全部事件（供 IM 文件状态复查）。</summary>
    public List<ActivityEvent> QueryEventsByType(string type, DateTime since)
    {
        var list = new List<ActivityEvent>();
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, type, name, path, size_bytes, url, source, version, old_version, occurred_at, note, extra
            FROM events
            WHERE type=@t AND occurred_at >= @since
            ORDER BY id DESC LIMIT 2000
            """;
        cmd.Parameters.AddWithValue("@t", type);
        cmd.Parameters.AddWithValue("@since", since.ToString("yyyy-MM-dd HH:mm:ss"));
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) list.Add(ReadEvent(reader));
        return list;
    }

    // ================= 内部状态键值表 =================

    /// <summary>读取内部状态值（浏览器游标等），不存在返回 null。</summary>
    public string? GetState(string key)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT value FROM {KvTable} WHERE key=@k";
        cmd.Parameters.AddWithValue("@k", key);
        return cmd.ExecuteScalar() as string;
    }

    /// <summary>写入内部状态值（UPSERT）。</summary>
    public void SetState(string key, string value)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"INSERT INTO {KvTable}(key, value) VALUES(@k, @v) ON CONFLICT(key) DO UPDATE SET value=@v";
        cmd.Parameters.AddWithValue("@k", key);
        cmd.Parameters.AddWithValue("@v", value);
        cmd.ExecuteNonQuery();
    }

    // ================= 工具 =================

    /// <summary>从 DataReader 组装 ActivityEvent 对象。</summary>
    private static ActivityEvent ReadEvent(SqliteDataReader r)
    {
        return new ActivityEvent
        {
            Id = r.GetInt64(0),
            Type = EventTypeExtensions.FromDbString(r.IsDBNull(1) ? null : r.GetString(1)),
            Name = r.IsDBNull(2) ? "" : r.GetString(2),
            Path = r.IsDBNull(3) ? null : r.GetString(3),
            SizeBytes = r.IsDBNull(4) ? null : r.GetInt64(4),
            Url = r.IsDBNull(5) ? null : r.GetString(5),
            Source = r.IsDBNull(6) ? null : r.GetString(6),
            Version = r.IsDBNull(7) ? null : r.GetString(7),
            OldVersion = r.IsDBNull(8) ? null : r.GetString(8),
            OccurredAt = DateTime.ParseExact(r.GetString(9), "yyyy-MM-dd HH:mm:ss", null),
            Note = r.IsDBNull(10) ? null : r.GetString(10),
            Extra = r.IsDBNull(11) ? null : r.GetString(11),
        };
    }

    /// <summary>把事件参数化绑定到 INSERT 命令（供 WriteQueue 批量插入复用）。</summary>
    public static void BindInsert(SqliteCommand cmd, ActivityEvent e, bool withId)
    {
        cmd.Parameters.Clear();
        if (withId) cmd.Parameters.AddWithValue("@id", e.Id);
        cmd.Parameters.AddWithValue("@type", e.Type.ToDbString());
        cmd.Parameters.AddWithValue("@name", e.Name);
        cmd.Parameters.AddWithValue("@path", (object?)e.Path ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@size", (object?)e.SizeBytes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@url", (object?)e.Url ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@source", (object?)e.Source ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ver", (object?)e.Version ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@oldver", (object?)e.OldVersion ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@at", e.OccurredAt.ToString("yyyy-MM-dd HH:mm:ss"));
        cmd.Parameters.AddWithValue("@note", (object?)e.Note ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@extra", (object?)e.Extra ?? DBNull.Value);
    }

    /// <summary>INSERT 语句文本（@id 版本供导入时保留原主键用）。</summary>
    public const string InsertSql =
        "INSERT INTO events(type,name,path,size_bytes,url,source,version,old_version,occurred_at,note,extra) " +
        "VALUES(@type,@name,@path,@size,@url,@source,@ver,@oldver,@at,@note,@extra)";
}
