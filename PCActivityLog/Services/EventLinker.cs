using PCActivityLog.Data;
using PCActivityLog.Models;

namespace PCActivityLog.Services;

/// <summary>
/// 下载↔安装关联器 —— 安装/更新事件出现时，回溯 48 小时内的下载记录，
/// 用"产品名 token 匹配安装包文件名"的方式找到来源安装包，互相写入对方的 extra。
/// 之后时间线里就能显示"该软件由 xxx.exe 安装"并可点击跳转。
/// </summary>
public class EventLinker
{
    private readonly Database _db;

    public EventLinker(Database db) => _db = db;

    /// <summary>安装/更新事件到达时调用（WriteQueue 的 Committed 之后才算稳妥，但这里直接查也安全：
    /// 查不到就不关联，下一款软件安装时会顺带再试。为了简单，在 Dispatch 时同步轻量执行）。</summary>
    public void OnInstallEvent(ActivityEvent installEvent)
    {
        try
        {
            var candidates = _db.QueryDownloadsSince(DateTime.Now.AddHours(-48));
            if (candidates.Count == 0) return;

            foreach (var dl in candidates)
            {
                var fileName = System.IO.Path.GetFileNameWithoutExtension(dl.Path ?? "");
                if (string.IsNullOrEmpty(fileName)) continue;

                if (NameMatches(installEvent.Name, fileName))
                {
                    // 双向写入关联 id
                    installEvent.SetExtra("linkedDownloadId", System.Text.Json.Nodes.JsonValue.Create(dl.Id));
                    _db.UpdateExtra(installEvent.Id, installEvent.Extra);

                    dl.SetExtra("linkedInstallId", System.Text.Json.Nodes.JsonValue.Create(installEvent.Id));
                    _db.UpdateExtra(dl.Id, dl.Extra);
                    DiagnosticsLog.Info($"事件关联: 安装\"{installEvent.Name}\" ← 下载\"{fileName}\"");
                    return; // 只关联最近的一个匹配
                }
            }
        }
        catch (Exception ex)
        {
            DiagnosticsLog.Warn("事件关联失败: " + ex.Message);
        }
    }

    /// <summary>
    /// 产品名与安装包文件名的模糊匹配（忽略大小写）：
    ///   1. 完整产品名作为子串出现在文件名 → 匹配（覆盖中文名与连续英文名）；
    ///   2. 否则把产品名拆成 token（≥2 字符），≥60% 的 token 出现在文件名 → 匹配。
    /// </summary>
    private static bool NameMatches(string productName, string fileName)
    {
        var p = productName.Trim().ToLowerInvariant();
        var f = fileName.Trim().ToLowerInvariant();
        if (p.Length < 2) return false;
        if (f.Contains(p)) return true;

        var tokens = Tokenize(p).Where(t => t.Length >= 2).Distinct().ToList();
        if (tokens.Count == 0) return false;
        var hit = tokens.Count(t => f.Contains(t));
        return hit * 100 >= tokens.Count * 60;
    }

    /// <summary>按非字母数字切分 token（保留中文连续段）。</summary>
    private static IEnumerable<string> Tokenize(string s)
    {
        var buffer = new List<char>();
        foreach (var ch in s)
        {
            if (char.IsLetterOrDigit(ch)) buffer.Add(ch);
            else if (buffer.Count > 0)
            {
                yield return new string(buffer.ToArray());
                buffer.Clear();
            }
        }
        if (buffer.Count > 0) yield return new string(buffer.ToArray());
    }
}
