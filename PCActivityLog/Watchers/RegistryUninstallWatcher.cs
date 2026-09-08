using Microsoft.Win32;
using PCActivityLog.Models;
using PCActivityLog.Services;

namespace PCActivityLog.Watchers;

/// <summary>
/// 软件监视模块 · 通道二：注册表 Uninstall 键对比。
/// 定时快照三处卸载注册表（HKLM 64 位、HKLM WOW6432Node、HKCU），
/// 与上一次快照对比，发现：
///   新增键 → 安装；键消失 → 卸载；DisplayVersion 变化 → 更新。
///
/// 防误报三道闸：
///   1. 跳过 SystemComponent=1（系统隐藏组件）与无 DisplayName 的键；
///   2. 变化先挂起，下轮轮询复核确认才产出（安装器写入是渐进的，
///      且部分软件运行时会临时改注册表）；
///   3. 与 MSI 通道通过共享 <see cref="RecentNameFilter"/> 交叉去重（10 分钟窗口）。
/// </summary>
public class RegistryUninstallWatcher : IDisposable
{
    private readonly AppSettings _settings;
    private readonly IEventSink _sink;
    private readonly RecentNameFilter _msiDedupe;

    private System.Threading.Timer? _pollTimer;

    /// <summary>上一次确认的快照：完整键路径 → 条目信息。</summary>
    private Dictionary<string, AppEntry> _lastSnapshot = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>待复核的候选变化。</summary>
    private List<PendingChange> _pendingChanges = new();

    /// <summary>一个已安装软件的注册表摘要。</summary>
    private record AppEntry(string DisplayName, string Version, string? Publisher, string? InstallLocation);

    /// <summary>待复核的候选变化。OldEntry 携带变化前的数据快照 ——
    /// 因为复核发生在下一轮（届时 _lastSnapshot 已被覆盖），必须在此刻记下旧值，
    /// 否则卸载事件拿不到软件名、更新事件拿不到旧版本号。</summary>
    private record PendingChange(string Key, ChangeKind Kind, AppEntry? Entry, AppEntry? OldEntry, int VerifyCount);

    private enum ChangeKind { Install, Uninstall, Update }

    /// <summary>
    /// 卸载信息的注册表路径（HKLM 下的原生路径；32 位视图访问同一路径会自动重定向到 WOW6432Node）。
    /// 不要在此再列 WOW6432Node —— 那会与 32 位视图重复采集同一批键。
    /// </summary>
    private const string NativeRoot = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    /// <summary>HKCU 下的卸载信息路径。</summary>
    private const string HkcuRoot = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    public RegistryUninstallWatcher(AppSettings settings, IEventSink sink, RecentNameFilter msiDedupe)
    {
        _settings = settings;
        _sink = sink;
        _msiDedupe = msiDedupe;
    }

    /// <summary>启动：先建立基线快照（不产生事件），然后按间隔轮询。</summary>
    public void Start()
    {
        Stop();
        _lastSnapshot = TakeSnapshot();
        _pendingChanges.Clear();
        _pollTimer = new System.Threading.Timer(_ => SafePoll(), null,
            TimeSpan.FromSeconds(_settings.RegistryPollSeconds),
            TimeSpan.FromSeconds(_settings.RegistryPollSeconds));
        DiagnosticsLog.Info($"注册表监视已启动，基线 {_lastSnapshot.Count} 个条目，轮询间隔 {_settings.RegistryPollSeconds}s");
    }

    /// <summary>停止：释放定时器、清空状态（下次启动重新建立基线，不回补）。</summary>
    public void Stop()
    {
        _pollTimer?.Dispose();
        _pollTimer = null;
        lock (_lock)
        {
            _pendingChanges.Clear();
            _lastSnapshot.Clear();
        }
    }

    public void Dispose() => Stop();

    // ================= 轮询与对比 =================

    /// <summary>一轮轮询：复核挂起变化 + 对比新快照。全程 try/catch。</summary>
    /// <summary>保护 _lastSnapshot / _pendingChanges 的锁（轮询线程与 Stop/Start 并发访问）。</summary>
    private readonly object _lock = new();

    /// <summary>重入闸：上一轮未结束则跳过本轮（扫描可能超过轮询间隔）。</summary>
    private int _polling;

    private void SafePoll()
    {
        // 重入保护：Timer 不保证回调串行，扫描耗时超过间隔时会并发进入
        if (Interlocked.CompareExchange(ref _polling, 1, 0) != 0) return;
        try
        {
            var current = TakeSnapshot();
            lock (_lock)
            {
                VerifyPending(current);
                DiffSnapshots(current);
                _lastSnapshot = current;
            }
        }
        catch (Exception ex)
        {
            DiagnosticsLog.Error("注册表轮询异常", ex);
        }
        finally
        {
            Interlocked.Exchange(ref _polling, 0);
        }
    }

    /// <summary>
    /// 扫描三处卸载注册表，返回 键路径→条目。
    ///
    /// 注意避免重复采集：`Registry64 + SOFTWARE\WOW6432Node\...` 与
    /// `Registry32 + SOFTWARE\Microsoft\...\Uninstall` 返回的是同一批键
    /// （32 位视图会自动重定向到 WOW6432Node）。此前两者都采，导致
    /// 每款 32 位软件在快照中出现两次，安装/卸载事件翻倍。
    /// 正确做法：HKLM64 只采原生路径，HKLM32 采其原生路径（自动重定向到 WOW6432Node）。
    /// </summary>
    private static Dictionary<string, AppEntry> TakeSnapshot()
    {
        var dict = new Dictionary<string, AppEntry>(StringComparer.OrdinalIgnoreCase);

        // HKCU：当前用户的卸载信息
        using (var hkcu = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default))
            Collect(dict, hkcu, HkcuRoot, "HKCU");

        // HKLM 64 位视图：64 位软件（原生路径）
        using (var hklm64 = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
            Collect(dict, hklm64, NativeRoot, "HKLM64");

        // HKLM 32 位视图：32 位软件（32 位视图下访问原生路径会自动重定向到 WOW6432Node）
        using (var hklm32 = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32))
            Collect(dict, hklm32, NativeRoot, "HKLM32");

        return dict;
    }

    /// <summary>读取一个根下的所有子键（读失败静默跳过单个键，不影响整体）。</summary>
    private static void Collect(Dictionary<string, AppEntry> dict, RegistryKey hive, string subPath, string tag)
    {
        using var root = hive.OpenSubKey(subPath);
        if (root is null) return;
        foreach (var name in root.GetSubKeyNames())
        {
            try
            {
                using var k = root.OpenSubKey(name);
                if (k is null) continue;

                // 隐藏系统组件不记录（Windows 自己的运行库等，纯噪音）
                if (k.GetValue("SystemComponent") is int sc && sc == 1) continue;

                var displayName = k.GetValue("DisplayName") as string;
                if (string.IsNullOrWhiteSpace(displayName)) continue; // 无显示名的键不是"已安装软件"

                var version = (k.GetValue("DisplayVersion") as string)?.Trim() ?? "";
                var publisher = k.GetValue("Publisher") as string;
                var location = k.GetValue("InstallLocation") as string;
                if (string.IsNullOrWhiteSpace(location)) location = null;

                dict[$"{tag}\\{name}"] = new AppEntry(displayName.Trim(), version, publisher?.Trim(), location);
            }
            catch { /* 单个键读取失败（权限/损坏）跳过 */ }
        }
    }

    /// <summary>对比上轮与本轮快照，产出候选变化（挂起等待复核）。</summary>
    private void DiffSnapshots(Dictionary<string, AppEntry> current)
    {
        foreach (var (key, entry) in current)
        {
            if (!_lastSnapshot.TryGetValue(key, out var old))
            {
                AddPending(new PendingChange(key, ChangeKind.Install, entry, null, 0));
            }
            else if (!string.Equals(old.Version, entry.Version, StringComparison.OrdinalIgnoreCase)
                     && !string.IsNullOrEmpty(entry.Version))
            {
                // 版本号变化 → 更新（新旧版本都记下来）
                AddPending(new PendingChange(key, ChangeKind.Update, entry, old, 0));
            }
        }
        foreach (var (key, old) in _lastSnapshot.Where(kv => !current.ContainsKey(kv.Key)))
            AddPending(new PendingChange(key, ChangeKind.Uninstall, null, old, 0));
    }

    /// <summary>复核挂起的变化：本轮状态与挂起时一致 → 确认产出；不一致 → 丢弃；超龄 → 丢弃。</summary>
    private void VerifyPending(Dictionary<string, AppEntry> current)
    {
        var still = new List<PendingChange>();
        foreach (var p in _pendingChanges)
        {
            // 3 轮复核（约 1.5 分钟）还没稳定就放弃，防止长期挂起占用内存
            if (p.VerifyCount >= 3) continue;

            var now = current.TryGetValue(p.Key, out var cur) ? cur : null;
            var confirmed = p.Kind switch
            {
                ChangeKind.Install => now is not null && SameEntry(now, p.Entry),
                ChangeKind.Uninstall => now is null,
                ChangeKind.Update => now is not null && p.Entry is not null
                    && string.Equals(now.Version, p.Entry.Version, StringComparison.OrdinalIgnoreCase),
                _ => false,
            };

            if (confirmed) Emit(p);
            else if (now is not null && p.Kind == ChangeKind.Install && p.Entry is not null
                     && !string.Equals(now.DisplayName, p.Entry.DisplayName, StringComparison.Ordinal))
            {
                still.Add(p with { Entry = now, VerifyCount = p.VerifyCount + 1 }); // 显示名还在变，更新后继续等
            }
            else if (now is not null && p.Kind != ChangeKind.Uninstall)
            {
                still.Add(p with { Entry = now, VerifyCount = p.VerifyCount + 1 }); // 继续等下一轮
            }
            // 状态已不同（如出现又消失）→ 直接丢弃
        }
        _pendingChanges = still;
    }

    /// <summary>挂起列表去重后加入。</summary>
    private void AddPending(PendingChange p)
    {
        if (_pendingChanges.Any(x => x.Key == p.Key && x.Kind == p.Kind)) return;
        _pendingChanges.Add(p);
    }

    /// <summary>两个条目是否基本一致（显示名与版本相同）。</summary>
    private static bool SameEntry(AppEntry a, AppEntry? b)
        => b is not null
           && string.Equals(a.DisplayName, b.DisplayName, StringComparison.Ordinal)
           && string.Equals(a.Version, b.Version, StringComparison.OrdinalIgnoreCase);

    /// <summary>复核通过，产出事件（受子开关与 MSI 去重约束）。</summary>
    private void Emit(PendingChange p)
    {
        // 名称与旧版本都从 PendingChange 自带的快照取，不依赖（已被覆盖的）_lastSnapshot
        var name = p.Kind switch
        {
            ChangeKind.Install => p.Entry!.DisplayName,
            ChangeKind.Update => p.Entry!.DisplayName,
            _ => p.OldEntry?.DisplayName ?? p.Key,
        };
        var oldVersion = p.Kind == ChangeKind.Update ? p.OldEntry?.Version : null;

        // 与 MSI 通道交叉去重：10 分钟内 MSI 已记过同名软件，这里不再重复记录
        if (_msiDedupe.ContainsRecent(name, TimeSpan.FromMinutes(10)))
            return;

        switch (p.Kind)
        {
            case ChangeKind.Install when !_settings.RecordInstalls:
            case ChangeKind.Uninstall when !_settings.RecordUninstalls:
            case ChangeKind.Update when !_settings.RecordUpdates:
                return;
        }

        _sink.Dispatch(new ActivityEvent
        {
            Type = p.Kind switch
            {
                ChangeKind.Install => EventType.Install,
                ChangeKind.Update => EventType.Update,
                _ => EventType.Uninstall,
            },
            Name = name,
            Version = p.Entry?.Version,
            OldVersion = oldVersion,
            Path = p.Entry?.InstallLocation,
            Source = "registry",
            OccurredAt = DateTime.Now,
        });
    }
}
