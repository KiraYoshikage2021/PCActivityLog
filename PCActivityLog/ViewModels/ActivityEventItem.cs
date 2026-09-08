using Microsoft.UI.Xaml.Media;
using PCActivityLog.Models;
using PCActivityLog.Services;
using Windows.UI;

namespace PCActivityLog.ViewModels;

/// <summary>
/// 时间线列表的一行（WinUI 版）—— 包装 <see cref="ActivityEvent"/>，提供界面绑定用的显示属性。
/// 徽章颜色在构造时按当前主题计算；主题切换后由 TimelineViewModel.Refresh() 重建行。
/// 与 WPF 版的差异：Brush 用 Microsoft.UI.Xaml.Media，Color 用 Windows.UI.Color（无需 Freeze）。
/// </summary>
public class ActivityEventItem
{
    public ActivityEvent Event { get; }

    public ActivityEventItem(ActivityEvent e)
    {
        Event = e;

        // IM 文件按来源配色（微信绿/QQ蓝），其余按类型色
        var c = e.Type == EventType.ImFile ? ImColor(e.Source) : TypeColor(e.Type);

        // 徽章配色：文字用基色（深色主题下提亮一档），底色用同色低透明度
        TypeBrush = new SolidColorBrush(ThemeService.IsDark ? ThemeService.Lighten(c, 0.30f) : c);
        TypeBadgeBrush = new SolidColorBrush(ThemeService.WithAlpha(c, ThemeService.IsDark ? (byte)0x3D : (byte)0x24));

        // 保存状态标识（仅 IM 文件有）：✔ 已保存 / ⚠ 已清理
        if (e.Type == EventType.ImFile)
        {
            var missing = e.GetExtraString("localStatus") == "missing";
            LocalStatusDisplay = missing ? "⚠ 已清理" : "✔ 已保存";
            var sc = missing ? Color.FromArgb(255, 0xD9, 0x77, 0x06) : Color.FromArgb(255, 0x16, 0xA3, 0x4A);
            LocalStatusBrush = new SolidColorBrush(ThemeService.IsDark ? ThemeService.Lighten(sc, 0.25f) : sc);
        }
        else
        {
            LocalStatusDisplay = "";
            LocalStatusBrush = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        }
    }

    /// <summary>IM 文件按来源取品牌色：微信绿 / QQ蓝 / 自定义灰。</summary>
    private static Color ImColor(string? source) => source switch
    {
        "wechat" => Color.FromArgb(255, 0x07, 0xC1, 0x60),
        "qq" => Color.FromArgb(255, 0x12, 0xB7, 0xF5),
        _ => Color.FromArgb(255, 0x64, 0x74, 0x8B),
    };

    public long Id => Event.Id;

    /// <summary>时间列（含秒）。</summary>
    public string TimeDisplay => Event.OccurredAt.ToString("yyyy-MM-dd HH:mm:ss");

    /// <summary>类型徽章文字（IM 文件按来源显示 微信文件/QQ文件）。</summary>
    public string TypeDisplay => Event.Type switch
    {
        EventType.ImFile => Event.Source switch
        {
            "wechat" => "微信文件",
            "qq" => "QQ文件",
            _ => "IM文件",
        },
        _ => Event.Type.ToDisplayName(),
    };

    /// <summary>类型徽章文字色。</summary>
    public Brush TypeBrush { get; }

    /// <summary>类型徽章底色。</summary>
    public Brush TypeBadgeBrush { get; }

    /// <summary>IM 文件的本地保存状态标识（其他类型为空）。</summary>
    public string LocalStatusDisplay { get; }

    /// <summary>保存状态标识颜色。</summary>
    public Brush LocalStatusBrush { get; }

    public string Name => Event.Name;

    /// <summary>详情列：下载/IM文件 → 大小；更新 → 旧→新版本；安装 → 版本。</summary>
    public string Detail
    {
        get
        {
            if ((Event.Type == EventType.Download || Event.Type == EventType.ImFile) && Event.SizeBytes is > 0)
            {
                var kb = Event.SizeBytes.Value / 1024.0;
                return kb >= 1024 ? $"{kb / 1024:F1} MB" : $"{kb:F0} KB";
            }
            if (Event.Type == EventType.Update && !string.IsNullOrEmpty(Event.OldVersion))
                return $"{Event.OldVersion} → {Event.Version ?? "?"}";
            return Event.Version ?? "";
        }
    }

    /// <summary>位置列：路径或网址。</summary>
    public string Location => Event.Path ?? Event.Url ?? "";

    /// <summary>关联的下载事件 id（安装事件的 extra 里），无则空。</summary>
    public long? LinkedDownloadId => Event.GetExtraLong("linkedDownloadId");

    /// <summary>关联提示文字（无关联为空）。</summary>
    public string LinkedHint => LinkedDownloadId is null ? "" : "🔗 来源安装包";

    /// <summary>来源显示（chrome/edge/firefox/msi/registry/manual…）。</summary>
    public string SourceDisplay => Event.Source ?? "";

    public string? Note => Event.Note;

    /// <summary>事件类型的主题色。</summary>
    public static Color TypeColor(EventType t) => t switch
    {
        EventType.Download => Color.FromArgb(255, 0x25, 0x63, 0xEB),
        EventType.FileDelete => Color.FromArgb(255, 0x64, 0x74, 0x8B),
        EventType.FileRename => Color.FromArgb(255, 0x47, 0x56, 0x69),
        EventType.ImFile => Color.FromArgb(255, 0x64, 0x74, 0x8B),
        EventType.Install => Color.FromArgb(255, 0x16, 0xA3, 0x4A),
        EventType.Update => Color.FromArgb(255, 0xD9, 0x77, 0x06),
        EventType.Uninstall => Color.FromArgb(255, 0xDC, 0x26, 0x26),
        EventType.Boot => Color.FromArgb(255, 0x7C, 0x3A, 0xED),
        EventType.Shutdown => Color.FromArgb(255, 0x93, 0x33, 0xEA),
        EventType.Restart => Color.FromArgb(255, 0x6D, 0x28, 0xD9),
        EventType.Sleep => Color.FromArgb(255, 0x63, 0x66, 0xF1),
        EventType.Wake => Color.FromArgb(255, 0x14, 0xB8, 0xA6),
        EventType.Browse => Color.FromArgb(255, 0x08, 0x91, 0xB2),
        _ => Color.FromArgb(255, 0x52, 0x52, 0x52),
    };
}
