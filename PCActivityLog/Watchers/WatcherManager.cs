using PCActivityLog.Data;
using PCActivityLog.Models;
using PCActivityLog.Services;

namespace PCActivityLog.Watchers;

/// <summary>
/// 模块管理器 —— 同时实现 <see cref="IEventSink"/>，是"模块产出事件"的唯一出口。
/// 职责：
///   1. 注册全部模块，按配置启动；
///   2. 设置页开关模块 → <see cref="SetModuleEnabled"/> 立即启停（先销毁旧实例资源再重建）；
///   3. 模块重启（配置变化后重建实例，杜绝旧对象残留引用）；
///   4. 事件汇入：写队列 → 气泡通知 → 安装事件触发下载关联匹配；
///   5. 程序退出时统一 <see cref="StopAll"/>（Dispose 清单）。
/// </summary>
public class WatcherManager : IEventSink, IDisposable
{
    private readonly AppSettings _settings;
    private readonly WriteQueue _writeQueue;
    private readonly Database _db;
    private readonly NotificationService? _notifier;
    private readonly EventLinker? _linker;
    private readonly object _lock = new();

    /// <summary>全部已注册模块（构造时固定顺序）。</summary>
    private readonly List<IWatcherModule> _modules = new();

    public WatcherManager(AppSettings settings, Database db, WriteQueue writeQueue,
        NotificationService? notifier, EventLinker? linker)
    {
        _settings = settings;
        _db = db;
        _writeQueue = writeQueue;
        _notifier = notifier;
        _linker = linker;

        // 关联匹配必须在事件"入库后"做（此时才有自增 Id，能反写对方的 extra）
        if (_linker != null)
            _writeQueue.EventsCommitted += OnEventsCommittedForLinking;
    }

    /// <summary>入库完成回调：对安装/更新事件做下载↔安装关联（配对解绑见 Dispose）。</summary>
    private void OnEventsCommittedForLinking(object? sender, IReadOnlyList<Models.ActivityEvent> events)
    {
        foreach (var e in events)
        {
            if (e.Type is Models.EventType.Install or Models.EventType.Update)
                _linker?.OnInstallEvent(e);
        }
    }

    /// <summary>创建并注册全部模块（不启动）。</summary>
    public void BuildModules()
    {
        lock (_lock)
        {
            _modules.Clear();
            _modules.Add(new DownloadWatcher(_settings, this));
            _modules.Add(new AppWatcher(_settings, _db, this));
            _modules.Add(new SystemEventWatcher(this, _db));
            // 浏览器历史记录功能已移除（用户要求不再采集，历史数据已清理）
            _modules.Add(new ImFileWatcher(_settings, this));
        }
    }

    /// <summary>按配置启动各模块。单个模块启动失败不影响其他模块。</summary>
    public void StartAll()
    {
        if (_modules.Count == 0) BuildModules();
        if (_settings.ModuleFileEnabled) SafeStart(Get("file"));
        if (_settings.ModuleAppEnabled) SafeStart(Get("app"));
        if (_settings.ModuleSystemEnabled) SafeStart(Get("system"));
        if (_settings.ModuleImEnabled) SafeStart(Get("imfile"));
    }

    /// <summary>启动模块（null 安全，模块不存在时忽略）。</summary>
    private static void SafeStart(IWatcherModule? m)
    {
        if (m is null) return;
        try { m.Start(); }
        catch (Exception ex) { DiagnosticsLog.Error($"模块 {m.DisplayName} 启动失败", ex); }
    }

    /// <summary>停止模块（null 安全）。</summary>
    private static void SafeStop(IWatcherModule? m)
    {
        if (m is null) return;
        try { m.Stop(); }
        catch (Exception ex) { DiagnosticsLog.Error($"模块 {m.DisplayName} 停止失败", ex); }
    }

    /// <summary>停止全部模块（退出程序时调用；已入库数据不受影响）。</summary>
    public void StopAll()
    {
        lock (_lock)
        {
            foreach (var m in _modules) SafeStop(m);
        }
    }

    /// <summary>运行中开关某模块（设置页调用，立即生效）。模块不存在时静默忽略。</summary>
    public void SetModuleEnabled(string moduleId, bool enabled)
    {
        var module = Get(moduleId);
        if (module is null)
        {
            DiagnosticsLog.Warn($"SetModuleEnabled：模块 {moduleId} 不存在，忽略");
            return;
        }
        if (enabled) SafeStart(module);
        else SafeStop(module);
    }

    /// <summary>模块配置变化后重启该模块（如改了监视文件夹列表、轮询间隔）。模块不存在时静默忽略。</summary>
    public void RestartModule(string moduleId)
    {
        var module = Get(moduleId);
        if (module is null)
        {
            DiagnosticsLog.Warn($"RestartModule：模块 {moduleId} 不存在，忽略");
            return;
        }
        SafeStop(module);
        SafeStart(module);
    }

    /// <summary>
    /// 按 Id 查找模块。找不到返回 null（容错）——调用方可能引用了已移除的模块
    /// （如历史遗留的 "browser"），此时应静默忽略而不是抛异常中断 UI 线程。
    /// </summary>
    private IWatcherModule? Get(string id)
    {
        lock (_lock) return _modules.FirstOrDefault(m => m.Id == id);
    }

    public Dictionary<string, bool> GetRunningStates()
    {
        lock (_lock) return _modules.ToDictionary(m => m.Id, m => m.IsRunning);
    }

    // ================= 事件汇入（IEventSink） =================

    /// <summary>
    /// 模块产出事件的统一入口：入库 → 通知。
    /// （下载↔安装关联改在 WriteQueue.EventsCommitted 里做，见构造函数。）
    /// 监视器线程调用，必须快进快出；任何一步失败都不影响入库。
    /// </summary>
    public void Dispatch(ActivityEvent e)
    {
        try
        {
            _writeQueue.Enqueue(e);            // 1. 入库（异步，不阻塞）
            _notifier?.OnEventArrived(e);       // 2. 气泡通知（内部有节流与 UI 编组）
        }
        catch (Exception ex)
        {
            DiagnosticsLog.Error("事件汇入异常", ex);
        }
    }

    public void Dispose()
    {
        if (_linker != null)
            _writeQueue.EventsCommitted -= OnEventsCommittedForLinking; // 配对解绑
        StopAll();
        lock (_lock)
        {
            foreach (var m in _modules) m.Dispose();
            _modules.Clear();
        }
    }
}
