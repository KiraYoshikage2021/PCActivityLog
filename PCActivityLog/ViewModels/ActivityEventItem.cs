using System.Windows.Media;
using PCActivityLog.Services;
using PCActivityLog.Models;

namespace PCActivityLog.ViewModels;

/// <summary>
/// 时间线列表的一行 —— 包装 <see cref="ActivityEvent"/>，提供界面绑定用的显示属性
/// （时间、类型徽章颜色、详情列等）。纯显示对象，不改数据。
/// 徽章颜色在构造时按当前主题计算并冻结；主题切换后由 MainViewModel.Refresh() 重建行。
/// </summary>
public class ActivityEventItem
{
    public ActivityEvent Event { get; }

    public ActivityEventItem(ActivityEvent e)
    {
        Event = e;

        // 徽章配色：文字用类型色（深色主题下提亮一档），底色用同色低透明度
        var c = TypeColor(e.Type);
        var textBrush = new SolidColorBrush(ThemeService.IsDark ? ThemeService.Lighten(c, 0.30f) : c);
        var bgBrush = new SolidColorBrush(ThemeService.WithAlpha(c, ThemeService.IsDark ? (byte)0x3D : (byte)0x24));
        textBrush.Freeze();
        bgBrush.Freeze();
        TypeBrush = textBrush;
        TypeBadgeBrush = bgBrush;
    }

    public long Id => Event.Id;

    /// <summary>时间列（含秒）。</summary>
    public string TimeDisplay => Event.OccurredAt.ToString("yyyy-MM-dd HH:mm:ss");

    /// <summary>类型徽章文字。</summary>
    public string TypeDisplay => Event.Type.ToDisplayName();

    /// <summary>类型徽章文字色（已冻结）。</summary>
    public Brush TypeBrush { get; }

    /// <summary>类型徽章底色（已冻结）。</summary>
    public Brush TypeBadgeBrush { get; }

    public string Name => Event.Name;

    /// <summary>详情列：下载 → 大小；更新 → 旧→新版本；安装 → 版本。</summary>
    public string Detail
    {
        get
        {
            if (Event.Type == EventType.Download && Event.SizeBytes is > 0)
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
    public string LinkedHint => LinkedDownloadId is null ? "" : "来源安装包 →";

    /// <summary>来源显示（chrome/edge/firefox/msi/registry/manual…）。</summary>
    public string SourceDisplay => Event.Source ?? "";

    public string? Note => Event.Note;

    /// <summary>事件类型的主题色。</summary>
    public static Color TypeColor(EventType t) => t switch
    {
        EventType.Download => Color.FromRgb(0x25, 0x63, 0xEB),   // 蓝
        EventType.FileDelete => Color.FromRgb(0x64, 0x74, 0x8B), // 灰蓝
        EventType.FileRename => Color.FromRgb(0x47, 0x56, 0x69), // 深灰蓝
        EventType.Install => Color.FromRgb(0x16, 0xA3, 0x4A),    // 绿
        EventType.Update => Color.FromRgb(0xD9, 0x77, 0x06),     // 琥珀
        EventType.Uninstall => Color.FromRgb(0xDC, 0x26, 0x26),  // 红
        EventType.Boot => Color.FromRgb(0x7C, 0x3A, 0xED),       // 紫
        EventType.Shutdown => Color.FromRgb(0x93, 0x33, 0xEA),   // 亮紫
        EventType.Browse => Color.FromRgb(0x08, 0x91, 0xB2),     // 青
        _ => Color.FromRgb(0x52, 0x52, 0x52),                    // 手动：深灰
    };
}
