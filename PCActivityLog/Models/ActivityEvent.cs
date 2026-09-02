using System.Text.Json;
using System.Text.Json.Nodes;

namespace PCActivityLog.Models;

/// <summary>
/// 一条活动事件 —— 本软件的核心数据模型，对应数据库 events 表的一行。
/// 不同类型的事件使用不同的字段（例如浏览事件只用 Name/Url，下载事件用 Path/SizeBytes/Url）。
/// </summary>
public class ActivityEvent
{
    /// <summary>数据库自增主键（未入库前为 0）。</summary>
    public long Id { get; set; }

    /// <summary>事件类型。</summary>
    public EventType Type { get; set; }

    /// <summary>名称：文件名 / 软件名 / 网页标题 / 手动记录的标题。</summary>
    public string Name { get; set; } = "";

    /// <summary>相关路径：文件完整路径 / 软件安装目录（可能为空）。</summary>
    public string? Path { get; set; }

    /// <summary>文件大小（字节），仅下载类事件使用。</summary>
    public long? SizeBytes { get; set; }

    /// <summary>网址：下载来源网址（Zone.Identifier）或浏览的网页地址。</summary>
    public string? Url { get; set; }

    /// <summary>事件来源：msi / registry / chrome / edge / firefox / manual / system / file。</summary>
    public string? Source { get; set; }

    /// <summary>软件版本（安装/更新事件），或文件扩展名（下载事件可复用）。</summary>
    public string? Version { get; set; }

    /// <summary>旧版本（仅更新事件使用）。</summary>
    public string? OldVersion { get; set; }

    /// <summary>事件发生时间（本地时间）。</summary>
    public DateTime OccurredAt { get; set; } = DateTime.Now;

    /// <summary>用户备注（可在列表中搜索）。</summary>
    public string? Note { get; set; }

    /// <summary>
    /// 附加信息 JSON 字符串。结构：
    /// { "renameOldPath": "旧路径", "linkedDownloadId": 123, "linkedInstallId": 456,
    ///   "hostUrl": "下载直链", "referrerUrl": "引荐页", "unexpected": true }
    /// </summary>
    public string? Extra { get; set; }

    // ---------- Extra JSON 的读写辅助 ----------

    /// <summary>向 Extra 写入一个键值（合并已有内容）。</summary>
    public void SetExtra(string key, JsonValue value)
    {
        var node = Extra is null ? new JsonObject() : (JsonObject?)JsonNode.Parse(Extra) ?? new JsonObject();
        node[key] = value;
        Extra = node.ToJsonString();
    }

    /// <summary>从 Extra 读取一个字符串值，不存在返回 null。</summary>
    public string? GetExtraString(string key)
    {
        if (Extra is null) return null;
        try
        {
            var node = JsonNode.Parse(Extra)?[key];
            return node?.GetValue<string>();
        }
        catch { return null; }
    }

    /// <summary>从 Extra 读取一个长整型值（如关联事件 id），不存在返回 null。</summary>
    public long? GetExtraLong(string key)
    {
        var s = GetExtraString(key);
        return long.TryParse(s, out var v) ? v : null;
    }

    /// <summary>用于日志/诊断的简短描述。</summary>
    public override string ToString() => $"{OccurredAt:yyyy-MM-dd HH:mm:ss} [{Type.ToDbString()}] {Name}";
}
