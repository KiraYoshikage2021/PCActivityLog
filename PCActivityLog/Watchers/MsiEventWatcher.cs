using System.Diagnostics.Eventing.Reader;
using System.Text.RegularExpressions;
using PCActivityLog.Data;
using PCActivityLog.Models;
using PCActivityLog.Services;

namespace PCActivityLog.Watchers;

/// <summary>
/// 软件监视模块 · 通道一：MSI 安装事件。
/// 订阅 Windows 应用程序日志的 MsiInstaller 事件：
///   1033 = 产品安装完成（插入字符串里带产品名/版本/状态码），11724 = 卸载成功。
///
/// 为什么安装通道用 1033 而不是 11707：1033 的插入字符串是结构化数据，
/// 能直接拿到产品版本号（不依赖本地化消息文案）；且两者一一对应、同秒成对出现，
/// 改用 1033 零覆盖损失还多拿版本号。
///
/// 防重复：Windows Installer 对"修复/重装"同样发 1033（版本号不变），
/// 典型如 VS 安装器内嵌的 Microsoft.NET.Workloads MSI 会在每次 SDK/VS 操作时重跑。
/// 同产品名 + 同版本的再次安装 → 视为修复，跳过记录（会话内缓存 + 数据库回查双保险）。
/// </summary>
public class MsiEventWatcher : IDisposable
{
    private const string XpathQuery =
        "*[System[Provider[@Name='MsiInstaller'] and (EventID=1033 or EventID=11724)]]";

    private readonly AppSettings _settings;
    private readonly IEventSink _sink;
    private readonly Database? _db;

    /// <summary>与注册表通道共享的去重过滤器（由软件监视模块持有并传入）。</summary>
    private readonly RecentNameFilter _dedupe;

    private EventLogWatcher? _watcher;

    /// <summary>本会话内已记录的"产品名|版本"（数据库回查覆盖不了同批未落库的事件，双保险）。</summary>
    private readonly HashSet<string> _seenInstalls = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>产品名提取正则：兼容中英文事件文案（11724 卸载事件没有结构化属性可用）。</summary>
    private static readonly Regex ProductRegex =
        new(@"(?:产品|Product)\s*[:：]\s*(.+?)\s*--", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public MsiEventWatcher(AppSettings settings, IEventSink sink, RecentNameFilter dedupe, Database? db = null)
    {
        _settings = settings;
        _sink = sink;
        _dedupe = dedupe;
        _db = db;
    }

    /// <summary>开始订阅。</summary>
    public void Start()
    {
        Stop(); // 自净
        try
        {
            var query = new EventLogQuery("Application", PathType.LogName, XpathQuery);
            _watcher = new EventLogWatcher(query);
            _watcher.EventRecordWritten += OnEvent;
            _watcher.Enabled = true;
            DiagnosticsLog.Info("MSI 事件订阅已启动（安装=1033 带版本号，卸载=11724）");
        }
        catch (Exception ex)
        {
            DiagnosticsLog.Error("MSI 事件订阅启动失败（不影响注册表通道）", ex);
        }
    }

    /// <summary>停止并释放（配对解绑 + Dispose）。</summary>
    public void Stop()
    {
        if (_watcher == null) return;
        _watcher.EventRecordWritten -= OnEvent;
        _watcher.Enabled = false;
        _watcher.Dispose();
        _watcher = null;
    }

    public void Dispose() => Stop();

    /// <summary>事件回调（系统线程池线程，try/catch 包裹）。</summary>
    private void OnEvent(object? sender, EventRecordWrittenEventArgs e)
    {
        try
        {
            var record = e.EventRecord;
            if (record is null) return;
            using (record) // EventRecord 实现 IDisposable，必须释放非托管句柄
            {
                HandleRecord(record);
            }
        }
        catch (Exception ex)
        {
            DiagnosticsLog.Error("处理 MSI 事件异常", ex);
        }
    }

    /// <summary>处理单条 MSI 事件记录（调用方负责 Dispose）。</summary>
    private void HandleRecord(EventRecord record)
    {
        if (record.Id == 1033)
        {
            HandleInstall(record);
            return;
        }
        HandleUninstall(record); // 11724
    }

    /// <summary>
    /// 安装事件（1033）：属性 [0]=产品名 [1]=版本 [2]=语言 [3]=状态码（0=成功）[4]=发布者。
    /// 同产品名 + 同版本的重复安装视为修复/重跑，跳过记录。
    /// </summary>
    private void HandleInstall(EventRecord record)
    {
        var props = record.Properties;
        var productName = props.Count > 0 ? props[0].Value?.ToString()?.Trim() : "";
        if (string.IsNullOrWhiteSpace(productName))
        {
            DiagnosticsLog.Warn("MSI 1033：未能提取产品名，跳过");
            return;
        }

        var status = props.Count > 3 ? props[3].Value?.ToString()?.Trim() : null;
        if (status is not ("0" or ""))
        {
            DiagnosticsLog.Warn($"MSI 安装失败（状态码 {status}），不记录: {productName}");
            return;
        }

        if (!_settings.RecordInstalls) return;

        var version = props.Count > 1 ? props[1].Value?.ToString()?.Trim() : "";

        // 同产品 + 同版本 → 修复/重跑（如 VS 安装器内嵌的 Workloads MSI），不重复入账
        if (!string.IsNullOrEmpty(version) && IsRepeatInstall(productName, version))
        {
            DiagnosticsLog.Info($"同版本重复安装，视为修复跳过: {productName} {version}");
            return;
        }

        if (!string.IsNullOrEmpty(version)) _seenInstalls.Add(productName + "|" + version);

        // 先登记进共享去重表，注册表通道稍后轮询到同一软件就不会重复记录
        _dedupe.Add(productName);
        _sink.Dispatch(new ActivityEvent
        {
            Type = EventType.Install,
            Name = productName,
            Version = string.IsNullOrEmpty(version) ? null : version,
            Source = "msi",
            OccurredAt = record.TimeCreated?.ToLocalTime() ?? DateTime.Now,
        });
    }

    /// <summary>同版本重复判定：本会话已记录过，或数据库里已有同名同版本的安装记录。</summary>
    private bool IsRepeatInstall(string productName, string version)
    {
        var key = productName + "|" + version;
        if (_seenInstalls.Contains(key)) return true;
        try
        {
            if (_db is not null && _db.HasInstallEvent(productName, version))
            {
                _seenInstalls.Add(key); // 回查命中也缓存，避免每次重复查库
                return true;
            }
        }
        catch (Exception ex)
        {
            DiagnosticsLog.Warn("查询历史安装记录失败（按未重复处理）: " + ex.Message);
        }
        return false;
    }

    /// <summary>卸载事件（11724）：从消息文案提取产品名（结构同旧逻辑）。</summary>
    private void HandleUninstall(EventRecord record)
    {
        var productName = ExtractProductName(record);
        if (string.IsNullOrWhiteSpace(productName))
        {
            DiagnosticsLog.Warn($"MSI 事件 {record.Id}：未能提取产品名，跳过");
            return;
        }

        if (!_settings.RecordUninstalls) return;

        // 登记进共享去重表（注册表通道的卸载事件交叉去重）
        _dedupe.Add(productName);
        _sink.Dispatch(new ActivityEvent
        {
            Type = EventType.Uninstall,
            Name = productName,
            Source = "msi",
            OccurredAt = record.TimeCreated?.ToLocalTime() ?? DateTime.Now,
        });
    }

    /// <summary>从事件记录中提取产品名：优先正则匹配消息文案，取不到再试第一个属性。</summary>
    private static string ExtractProductName(EventRecord record)
    {
        try
        {
            var message = record.FormatDescription() ?? "";
            var m = ProductRegex.Match(message);
            if (m.Success) return m.Groups[1].Value.Trim();

            // 后备：MsiInstaller 事件的第一个属性通常就是产品名
            if (record.Properties.Count > 0 && record.Properties[0].Value is string s)
                return s.Trim();
        }
        catch { /* FormatDescription 可能因缺消息 DLL 抛异常 */ }
        return "";
    }
}
