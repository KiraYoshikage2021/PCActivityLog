using System.Diagnostics.Eventing.Reader;
using System.Text.RegularExpressions;
using PCActivityLog.Models;
using PCActivityLog.Services;

namespace PCActivityLog.Watchers;

/// <summary>
/// 软件监视模块 · 通道一：MSI 安装事件。
/// 订阅 Windows 应用程序事件日志的 MsiInstaller 事件：
///   11707 = 安装成功，11724 = 卸载成功。
/// 这是覆盖 MSI 安装包的最直接信号（注册表通道作为兜底覆盖 EXE 安装器）。
/// 事件文案中包含产品名，如 "产品: XXX -- 安装成功。"，用正则提取。
/// </summary>
public class MsiEventWatcher : IDisposable
{
    private const string XpathQuery =
        "*[System[Provider[@Name='MsiInstaller'] and (EventID=11707 or EventID=11724)]]";

    private readonly AppSettings _settings;
    private readonly IEventSink _sink;

    /// <summary>与注册表通道共享的去重过滤器（由软件监视模块持有并传入）。</summary>
    private readonly RecentNameFilter _dedupe;

    private EventLogWatcher? _watcher;

    /// <summary>产品名提取正则：兼容中英文事件文案。</summary>
    private static readonly Regex ProductRegex =
        new(@"(?:产品|Product)\s*[:：]\s*(.+?)\s*--", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public MsiEventWatcher(AppSettings settings, IEventSink sink, RecentNameFilter dedupe)
    {
        _settings = settings;
        _sink = sink;
        _dedupe = dedupe;
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
            DiagnosticsLog.Info("MSI 事件订阅已启动");
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
        var productName = ExtractProductName(record);
        if (string.IsNullOrWhiteSpace(productName))
        {
            DiagnosticsLog.Warn($"MSI 事件 {record.Id}：未能提取产品名，跳过");
            return;
        }

        // 先登记进共享去重表，注册表通道稍后轮询到同一软件就不会重复记录
        _dedupe.Add(productName);

        var isInstall = record.Id == 11707;
        if ((isInstall && !_settings.RecordInstalls) || (!isInstall && !_settings.RecordUninstalls))
            return;

        _sink.Dispatch(new ActivityEvent
        {
            Type = isInstall ? EventType.Install : EventType.Uninstall,
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
