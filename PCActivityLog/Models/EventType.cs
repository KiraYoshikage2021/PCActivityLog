namespace PCActivityLog.Models;

/// <summary>
/// 事件类型枚举 —— 数据库中以小写字符串形式存储，新增类型时同步更新
/// <see cref="EventTypeExtensions.ToDbString"/> 和 <see cref="EventTypeExtensions.FromDbString"/>。
/// </summary>
public enum EventType
{
    /// <summary>文件下载完成（监视文件夹中出现的、带来源网址的新文件）</summary>
    Download,

    /// <summary>文件被删除</summary>
    FileDelete,

    /// <summary>文件被重命名（或移动到同监视范围内的新路径）</summary>
    FileRename,

    /// <summary>软件安装</summary>
    Install,

    /// <summary>软件更新（已安装软件的版本号变化）</summary>
    Update,

    /// <summary>软件卸载</summary>
    Uninstall,

    /// <summary>电脑开机</summary>
    Boot,

    /// <summary>电脑关机</summary>
    Shutdown,

    /// <summary>浏览器网页访问</summary>
    Browse,

    /// <summary>用户手动补录的事件</summary>
    Manual,
}

/// <summary>EventType 的辅助方法：数据库字符串互转、中文显示名、所属大类。</summary>
public static class EventTypeExtensions
{
    /// <summary>枚举 → 数据库存储字符串。</summary>
    public static string ToDbString(this EventType t) => t switch
    {
        EventType.Download => "download",
        EventType.FileDelete => "file_delete",
        EventType.FileRename => "file_rename",
        EventType.Install => "install",
        EventType.Update => "update",
        EventType.Uninstall => "uninstall",
        EventType.Boot => "boot",
        EventType.Shutdown => "shutdown",
        EventType.Browse => "browse",
        EventType.Manual => "manual",
        _ => t.ToString().ToLowerInvariant(),
    };

    /// <summary>数据库字符串 → 枚举。无法识别时返回 Manual（容错，避免脏数据导致崩溃）。</summary>
    public static EventType FromDbString(string? s) => s switch
    {
        "download" => EventType.Download,
        "file_delete" => EventType.FileDelete,
        "file_rename" => EventType.FileRename,
        "install" => EventType.Install,
        "update" => EventType.Update,
        "uninstall" => EventType.Uninstall,
        "boot" => EventType.Boot,
        "shutdown" => EventType.Shutdown,
        "browse" => EventType.Browse,
        "manual" => EventType.Manual,
        _ => EventType.Manual,
    };

    /// <summary>界面显示用的中文名称。</summary>
    public static string ToDisplayName(this EventType t) => t switch
    {
        EventType.Download => "下载",
        EventType.FileDelete => "删除",
        EventType.FileRename => "重命名",
        EventType.Install => "安装",
        EventType.Update => "更新",
        EventType.Uninstall => "卸载",
        EventType.Boot => "开机",
        EventType.Shutdown => "关机",
        EventType.Browse => "浏览",
        EventType.Manual => "手动",
        _ => t.ToString(),
    };

    /// <summary>按事件类型分组后的结果（供时间线筛选下拉框使用）。</summary>
    public static EventGroup ToGroup(this EventType t) => t switch
    {
        EventType.Download => EventGroup.Download,
        EventType.FileDelete or EventType.FileRename => EventGroup.FileOps,
        EventType.Install or EventType.Update or EventType.Uninstall => EventGroup.App,
        EventType.Boot or EventType.Shutdown => EventGroup.System,
        EventType.Browse => EventGroup.Browse,
        EventType.Manual => EventGroup.Manual,
        _ => EventGroup.Manual,
    };
}

/// <summary>事件大类 —— 对应时间线页的筛选标签。</summary>
public enum EventGroup
{
    All,       // 全部
    Download,  // 下载
    FileOps,   // 文件操作（删除/重命名）
    App,       // 应用（安装/更新/卸载）
    System,    // 系统（开机/关机）
    Browse,    // 浏览
    Manual,    // 手动
}

/// <summary>EventGroup 的辅助方法。</summary>
public static class EventGroupExtensions
{
    /// <summary>大类的中文显示名（用于筛选下拉框）。</summary>
    public static string ToDisplayName(this EventGroup g) => g switch
    {
        EventGroup.All => "全部",
        EventGroup.Download => "下载",
        EventGroup.FileOps => "文件操作",
        EventGroup.App => "应用",
        EventGroup.System => "系统",
        EventGroup.Browse => "浏览",
        EventGroup.Manual => "手动",
        _ => g.ToString(),
    };

    /// <summary>该大类包含哪些数据库事件类型字符串（用于 SQL IN 条件）。</summary>
    public static string[] ToDbTypeList(this EventGroup g)
    {
        return g switch
        {
            EventGroup.Download => new[] { "download" },
            EventGroup.FileOps => new[] { "file_delete", "file_rename" },
            EventGroup.App => new[] { "install", "update", "uninstall" },
            EventGroup.System => new[] { "boot", "shutdown" },
            EventGroup.Browse => new[] { "browse" },
            EventGroup.Manual => new[] { "manual" },
            _ => Array.Empty<string>(), // All = 空 = 不过滤
        };
    }
}
