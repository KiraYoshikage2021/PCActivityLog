using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PCActivityLog.Services;

/// <summary>
/// 应用配置 —— 全部开关与参数都集中在这里，持久化为 JSON 文件
/// （%LOCALAPPDATA%\PCActivityLog\settings.json）。
/// 修改任何设置后调用 <see cref="Save"/> 立即生效并保存。
/// </summary>
public class AppSettings
{
    // ---------- 模块总开关 ----------

    /// <summary>文件监视模块总开关（下载/删除/重命名的父开关）。</summary>
    public bool ModuleFileEnabled { get; set; } = true;

    /// <summary>软件监视模块总开关（安装/更新/卸载的父开关）。</summary>
    public bool ModuleAppEnabled { get; set; } = true;

    /// <summary>系统监视模块总开关（开机/关机记录）。</summary>
    public bool ModuleSystemEnabled { get; set; } = true;

    /// <summary>浏览器记录模块（已移除该功能，保留字段仅为兼容旧配置文件）。</summary>
    public bool ModuleBrowserEnabled { get; set; } = false;

    // ---------- 微信/QQ 文件监视子开关与参数 ----------

    /// <summary>微信/QQ 文件监视模块总开关。</summary>
    public bool ModuleImEnabled { get; set; } = true;

    /// <summary>是否监视微信收到的文件。</summary>
    public bool ImWechat { get; set; } = true;

    /// <summary>是否监视 QQ 收到的文件。</summary>
    public bool ImQq { get; set; } = true;

    /// <summary>额外自定义的 IM 文件目录（自动识别不到时手工补充，递归监视）。</summary>
    public List<string> ImFolders { get; set; } = new();

    /// <summary>时间线各列宽度（键 = 列标题文本，值 = 像素），用户拖拽后记忆，重启保持。</summary>
    public Dictionary<string, double> ColumnWidths { get; set; } = new();

    // ---------- 文件监视子开关与参数 ----------

    /// <summary>是否记录下载。</summary>
    public bool RecordDownloads { get; set; } = true;

    /// <summary>是否记录删除。</summary>
    public bool RecordDeletes { get; set; } = true;

    /// <summary>是否记录重命名。</summary>
    public bool RecordRenames { get; set; } = true;

    /// <summary>监视的文件夹列表（默认为系统"下载"文件夹）。</summary>
    public List<string> WatchedFolders { get; set; } = new();

    /// <summary>下载落盘判定窗口（秒）：文件大小持续稳定这么久才算下载完成。</summary>
    public int SettleSeconds { get; set; } = 4;

    // ---------- 软件监视子开关与参数 ----------

    public bool RecordInstalls { get; set; } = true;
    public bool RecordUpdates { get; set; } = true;
    public bool RecordUninstalls { get; set; } = true;

    /// <summary>注册表轮询间隔（秒）。</summary>
    public int RegistryPollSeconds { get; set; } = 30;

    // ---------- 浏览器记录子开关与参数 ----------

    public bool BrowserChrome { get; set; } = true;
    public bool BrowserEdge { get; set; } = true;
    public bool BrowserFirefox { get; set; } = true;

    /// <summary>浏览器历史轮询间隔（分钟）。</summary>
    public int BrowserPollMinutes { get; set; } = 2;

    /// <summary>浏览记录保留天数（0 = 永久）。</summary>
    public int BrowserRetentionDays { get; set; } = 90;

    /// <summary>除浏览外其他事件的保留天数（0 = 永久）。</summary>
    public int OtherRetentionDays { get; set; } = 0;

    // ---------- 通知 ----------

    /// <summary>气泡通知总开关。</summary>
    public bool NotificationsEnabled { get; set; } = true;

    /// <summary>下载事件是否弹气泡。</summary>
    public bool NotifyDownloads { get; set; } = true;

    /// <summary>应用事件（安装/更新/卸载）是否弹气泡。</summary>
    public bool NotifyApp { get; set; } = true;

    /// <summary>系统事件（开关机）是否弹气泡。</summary>
    public bool NotifySystem { get; set; } = false;

    /// <summary>浏览事件是否弹气泡（默认关，浏览太频繁会刷屏）。</summary>
    public bool NotifyBrowse { get; set; } = false;

    // ---------- 行为 ----------

    /// <summary>主题模式：auto = 跟随系统（默认）/ light / dark。</summary>
    public string ThemeMode { get; set; } = "auto";

    /// <summary>点击窗口关闭按钮时：true = 最小化到托盘；false = 直接退出程序。</summary>
    public bool MinimizeToTrayOnClose { get; set; } = true;

    /// <summary>开机自启动。</summary>
    public bool StartWithWindows { get; set; } = false;

    // ---------- 持久化 ----------

    private static readonly string SettingsFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PCActivityLog", "settings.json");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // 中文不转义
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>从磁盘加载配置；文件不存在或损坏时返回带默认值的新实例（绝不抛异常）。</summary>
    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsFile))
            {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsFile), JsonOpts);
                if (s != null)
                {
                    s.FixDefaults();
                    return s;
                }
            }
        }
        catch (Exception ex)
        {
            DiagnosticsLog.Warn("读取配置失败，使用默认配置: " + ex.Message);
        }
        var fresh = new AppSettings();
        fresh.FixDefaults();
        return fresh;
    }

    /// <summary>保存用的锁（UI 线程与托盘线程都可能调用 Save）。</summary>
    private static readonly object SaveLock = new();

    /// <summary>
    /// 保存到磁盘（原子写：先写临时文件再替换，避免写一半崩溃导致配置损坏）。
    /// 损坏的配置会被 Load 回退到全新默认值，等于丢失用户全部设置，所以必须原子写。
    /// </summary>
    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(SettingsFile)!;
            Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(this, JsonOpts);

            lock (SaveLock)
            {
                var tmp = SettingsFile + ".tmp";
                File.WriteAllText(tmp, json, System.Text.Encoding.UTF8);
                // File.Move(overwrite:true) 在同一卷上是原子替换
                File.Move(tmp, SettingsFile, overwrite: true);
            }
        }
        catch (Exception ex)
        {
            DiagnosticsLog.Warn("保存配置失败: " + ex.Message);
        }
    }

    /// <summary>校正不合法的值（如轮询间隔过小、监视文件夹为空），保证程序行为始终可预期。</summary>
    private void FixDefaults()
    {
        if (WatchedFolders.Count == 0)
        {
            var downloads = KnownFolders.GetDownloadsPath();
            if (!string.IsNullOrEmpty(downloads)) WatchedFolders.Add(downloads);
        }
        WatchedFolders = WatchedFolders.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        ImFolders = ImFolders.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        SettleSeconds = Math.Clamp(SettleSeconds, 2, 60);
        RegistryPollSeconds = Math.Clamp(RegistryPollSeconds, 10, 3600);
        BrowserPollMinutes = Math.Clamp(BrowserPollMinutes, 1, 120);
        BrowserRetentionDays = Math.Max(0, BrowserRetentionDays);
        OtherRetentionDays = Math.Max(0, OtherRetentionDays);
        // 注：ColumnWidths 字段保留仅为向前兼容旧配置文件（列宽拖拽功能已随 DataGrid 移除）。
    }
}

/// <summary>获取系统已知文件夹（下载文件夹等），兼容 OneDrive 重定向。</summary>
public static class KnownFolders
{
    /// <summary>获取当前用户的"下载"文件夹路径；失败时退回 桌面\下载 或 用户目录。</summary>
    public static string GetDownloadsPath()
    {
        try
        {
            // 用户 Shell 目录注册表（会被 OneDrive/自定义位置重定向更新）
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders");
            if (key?.GetValue("{374DE290-123F-4565-9164-39C4925E467B}") is string v && !string.IsNullOrEmpty(v))
                return Environment.ExpandEnvironmentVariables(v);
        }
        catch { /* 注册表读取失败时走后备路径 */ }

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var fallback = Path.Combine(userProfile, "Downloads");
        return Directory.Exists(fallback) ? fallback : userProfile;
    }
}
