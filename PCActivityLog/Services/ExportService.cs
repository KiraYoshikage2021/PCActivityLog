using System.IO;
using System.Text;
using System.Text.Json;
using PCActivityLog.Data;
using PCActivityLog.Models;

namespace PCActivityLog.Services;

/// <summary>
/// 导出服务 —— 把当前筛选条件下的全部事件导出为 CSV（带 UTF-8 BOM，
/// Excel 直接打开中文不乱码）或 JSON。分批读取，避免一次性载入内存。
/// </summary>
public class ExportService
{
    private readonly Database _db;

    public ExportService(Database db) => _db = db;

    /// <summary>导出 CSV，返回导出条数；取消返回 -1。</summary>
    public int ExportCsv(Database.QueryFilter filter, string filePath)
    {
        var count = 0;
        using var writer = new StreamWriter(filePath, false, new UTF8Encoding(true)); // true = BOM
        // 表头
        writer.WriteLine("时间,类型,名称,大小/版本,路径,网址,来源,备注");

        foreach (var batch in ReadBatches(filter))
        {
            foreach (var e in batch)
            {
                writer.WriteLine(string.Join(",",
                    Csv(e.OccurredAt.ToString("yyyy-MM-dd HH:mm:ss")),
                    Csv(e.Type.ToDisplayName()),
                    Csv(e.Name),
                    Csv(DetailOf(e)),
                    Csv(e.Path ?? ""),
                    Csv(e.Url ?? ""),
                    Csv(e.Source ?? ""),
                    Csv(e.Note ?? "")));
                count++;
            }
        }
        return count;
    }

    /// <summary>导出 JSON（数组），返回导出条数。</summary>
    public int ExportJson(Database.QueryFilter filter, string filePath)
    {
        var count = 0;
        using var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });

        writer.WriteStartArray();
        foreach (var batch in ReadBatches(filter))
        {
            foreach (var e in batch)
            {
                writer.WriteStartObject();
                writer.WriteString("time", e.OccurredAt.ToString("yyyy-MM-dd HH:mm:ss"));
                writer.WriteString("type", e.Type.ToDbString());
                writer.WriteString("name", e.Name);
                writer.WriteString("detail", DetailOf(e));
                if (e.Path != null) writer.WriteString("path", e.Path);
                if (e.Url != null) writer.WriteString("url", e.Url);
                if (e.Source != null) writer.WriteString("source", e.Source);
                if (e.Version != null) writer.WriteString("version", e.Version);
                if (e.OldVersion != null) writer.WriteString("oldVersion", e.OldVersion);
                if (e.Note != null) writer.WriteString("note", e.Note);
                if (e.Extra != null)
                {
                    writer.WritePropertyName("extra");
                    writer.WriteRawValue(e.Extra); // extra 本身就是合法 JSON，原样嵌入
                }
                writer.WriteEndObject();
                count++;
            }
        }
        writer.WriteEndArray();
        return count;
    }

    /// <summary>详情列：下载显示大小，安装/更新显示版本，其他留空。</summary>
    private static string DetailOf(ActivityEvent e)
    {
        if (e.Type == EventType.Download && e.SizeBytes is > 0)
        {
            var kb = e.SizeBytes.Value / 1024.0;
            return kb >= 1024 ? $"{kb / 1024:F1} MB" : $"{kb:F0} KB";
        }
        if (e.Type == EventType.Update && !string.IsNullOrEmpty(e.OldVersion))
            return $"{e.OldVersion} → {e.Version ?? "?"}";
        return e.Version ?? "";
    }

    /// <summary>CSV 字段转义：包引号，内部引号翻倍。</summary>
    private static string Csv(string s) => "\"" + s.Replace("\"", "\"\"") + "\"";

    /// <summary>分批迭代（每批 1000 条），大数据量导出不占内存。</summary>
    private IEnumerable<List<ActivityEvent>> ReadBatches(Database.QueryFilter filter)
    {
        const int size = 1000;
        for (var offset = 0; ; offset += size)
        {
            var batch = _db.QueryEvents(filter with { Offset = offset, Limit = size });
            if (batch.Count == 0) yield break;
            yield return batch;
            if (batch.Count < size) yield break;
        }
    }
}
