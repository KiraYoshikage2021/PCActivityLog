using System.Windows;
using System.Windows.Threading;
using Hardcodet.Wpf.TaskbarNotification;
using PCActivityLog.Models;

namespace PCActivityLog.Services;

/// <summary>
/// 气泡通知服务 —— 新事件到达时在托盘图标上弹气泡。
/// 约束：
///   1. 节流：两次气泡至少间隔 10 秒，期间的事件不弹（防刷屏）；
///   2. 每类事件是否弹气泡由设置控制（浏览/系统默认关）；
///   3. 所有 UI 操作经 Dispatcher 编组到 UI 线程（规范第 8 条）；
///   4. 事件到达高峰时合并：被节流掉的事件计数并入下一条气泡文案。
/// </summary>
public class NotificationService : IDisposable
{
    private readonly AppSettings _settings;
    private readonly Func<TaskbarIcon?> _trayGetter;
    private readonly Dispatcher _uiDispatcher;

    private readonly object _lock = new();
    private DateTime _lastShown = DateTime.MinValue;
    private int _suppressedCount;

    public NotificationService(AppSettings settings, Func<TaskbarIcon?> trayGetter)
    {
        _settings = settings;
        _trayGetter = trayGetter;
        _uiDispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
    }

    /// <summary>事件到达（任意线程可调用）。</summary>
    public void OnEventArrived(ActivityEvent e)
    {
        if (!_settings.NotificationsEnabled) return;
        if (!ShouldNotify(e.Type)) return;

        lock (_lock)
        {
            var sinceLast = DateTime.Now - _lastShown;
            if (sinceLast < TimeSpan.FromSeconds(10))
            {
                _suppressedCount++;
                return; // 节流窗口内，静默丢弃并计数
            }
            _lastShown = DateTime.Now;
        }

        var suppressed = _suppressedCount;
        _suppressedCount = 0;

        _uiDispatcher.BeginInvoke(() =>
        {
            try
            {
                var tray = _trayGetter();
                var text = $"{e.Type.ToDisplayName()}: {Trim(e.Name, 80)}" +
                           (suppressed > 0 ? $"\n（还有 {suppressed} 条新事件）" : "");
                tray?.ShowBalloonTip("电脑日志记录", text, BalloonIcon.Info);
            }
            catch { /* 托盘未就绪时静默 */ }
        });
    }

    /// <summary>按事件类型查通知子开关。</summary>
    private bool ShouldNotify(EventType t) => t switch
    {
        EventType.Download => _settings.NotifyDownloads,
        EventType.Install or EventType.Update or EventType.Uninstall => _settings.NotifyApp,
        EventType.Boot or EventType.Shutdown => _settings.NotifySystem,
        EventType.Browse => _settings.NotifyBrowse,
        _ => false, // 删除/重命名/手动记录不弹气泡（太频繁/无必要）
    };

    private static string Trim(string s, int max) => s.Length <= max ? s : s[..max] + "…";

    public void Dispose() { /* 无需释放的资源；方法保留给调用方统一写法 */ }
}
