using PCActivityLog.Data;
using PCActivityLog.Services;

namespace PCActivityLog.Watchers;

/// <summary>
/// 软件监视模块 —— 组合两条通道（MSI 事件 + 注册表对比），对外是一个 IWatcherModule。
/// 两通道共享一个 <see cref="RecentNameFilter"/> 做交叉去重。
/// 子开关（安装/更新/卸载）由各通道在产出事件时即时读取，切换立即生效。
/// </summary>
public class AppWatcher : IWatcherModule
{
    public string Id => "app";
    public string DisplayName => "软件监视";

    private readonly MsiEventWatcher _msi;
    private readonly RegistryUninstallWatcher _registry;
    private readonly RecentNameFilter _dedupe = new();

    public AppWatcher(AppSettings settings, Database? db, IEventSink sink)
    {
        _msi = new MsiEventWatcher(settings, sink, _dedupe, db);
        _registry = new RegistryUninstallWatcher(settings, sink, _dedupe);
    }

    public bool IsRunning { get; private set; }

    public void Start()
    {
        Stop();
        _registry.Start();
        _msi.Start();
        _dedupe.Clear();
        IsRunning = true;
    }

    public void Stop()
    {
        IsRunning = false;
        _msi.Stop();
        _registry.Stop();
        _dedupe.Clear();
    }

    public void Dispose()
    {
        Stop();
        _msi.Dispose();
        _registry.Dispose();
    }
}
