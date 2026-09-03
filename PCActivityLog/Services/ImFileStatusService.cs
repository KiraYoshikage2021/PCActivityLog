using System.IO;
using PCActivityLog.Data;

namespace PCActivityLog.Services;

/// <summary>
/// IM 文件保存状态复查服务 —— 定期复核最近收到的微信/QQ 文件是否仍在本地。
///
/// 背景：微信/QQ 的存储管理可能自动清理旧文件（或用户手动清理），
/// "是否已保存到本地"会随时间失效。本服务查询最近 14 天的 im_file 事件，
/// 逐个 File.Exists 复核，把 extra 里的 localStatus 在 saved/missing 间翻转。
///
/// 节奏：启动 90 秒后首次复查（兼顾用户体验与开发验证），之后每 30 分钟一轮。
/// 每轮事件量很小（14 天内的 IM 文件），逐条更新无性能压力。
/// </summary>
public class ImFileStatusService : IDisposable
{
    private readonly Database _db;
    private System.Threading.Timer? _timer;

    public ImFileStatusService(Database db) => _db = db;

    public void Start()
    {
        Stop();
        _timer = new System.Threading.Timer(_ => SafeCheck(), null,
            TimeSpan.FromSeconds(90), TimeSpan.FromMinutes(30));
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }

    public void Dispose() => Stop();

    /// <summary>一轮复查（全程 try/catch，绝不影响主程序）。</summary>
    private void SafeCheck()
    {
        try
        {
            var events = _db.QueryEventsByType("im_file", DateTime.Now.AddDays(-14));
            var changed = 0;
            foreach (var e in events)
            {
                var desired = !string.IsNullOrEmpty(e.Path) && File.Exists(e.Path) ? "saved" : "missing";
                var current = e.GetExtraString("localStatus") ?? "saved";
                if (current == desired) continue;

                e.SetExtra("localStatus", System.Text.Json.Nodes.JsonValue.Create(desired));
                _db.UpdateExtra(e.Id, e.Extra);
                changed++;
            }
            if (changed > 0)
                DiagnosticsLog.Info($"IM 文件状态复查：{changed} 条状态更新");
        }
        catch (Exception ex)
        {
            DiagnosticsLog.Error("IM 文件状态复查异常", ex);
        }
    }
}
