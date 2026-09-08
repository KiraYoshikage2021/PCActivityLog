using PCActivityLog.Models;

namespace PCActivityLog.Services;

/// <summary>
/// 气泡通知服务（WinUI 版）—— 由托盘图标展示新事件气泡。
/// 阶段 3 接入 H.NotifyIcon 后完善；当前提供与 WPF 版一致的接口签名，
/// 保证 WatcherManager 等纯逻辑层可直接编译。
/// </summary>
public class NotificationService : IDisposable
{
    private readonly AppSettings _settings;
    private readonly object _lock = new();
    private DateTime _lastShown = DateTime.MinValue;
    private int _suppressed;

    /// <summary>展示气泡的委托（由 App 在托盘就绪后注入；未注入则静默丢弃）。</summary>
    public Action<string, string>? ShowBalloon { get; set; }

    public NotificationService(AppSettings settings) => _settings = settings;

    /// <summary>事件到达（任意线程可调用）。10 秒节流，被节流的事件合并计数到下一条。</summary>
    public void OnEventArrived(ActivityEvent e)
    {
        if (!_settings.NotificationsEnabled) return;
        if (!ShouldNotify(e.Type)) return;

        lock (_lock)
        {
            if (DateTime.Now - _lastShown < TimeSpan.FromSeconds(10)) { _suppressed++; return; }
            _lastShown = DateTime.Now;
        }
        var extra = _suppressed; _suppressed = 0;

        var title = "电脑日志记录";
        var text = $"{e.Type.ToDisplayName()}: {Trim(e.Name, 80)}" + (extra > 0 ? $"\n（还有 {extra} 条新事件）" : "");
        ShowBalloon?.Invoke(title, text);
    }

    private bool ShouldNotify(EventType t) => t switch
    {
        EventType.Download => _settings.NotifyDownloads,
        EventType.Install or EventType.Update or EventType.Uninstall => _settings.NotifyApp,
        EventType.Boot or EventType.Shutdown => _settings.NotifySystem,
        EventType.Browse => _settings.NotifyBrowse,
        _ => false,
    };

    private static string Trim(string s, int max) => s.Length <= max ? s : s[..max] + "…";

    public void Dispose() { }
}
