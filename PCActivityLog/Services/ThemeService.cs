using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace PCActivityLog.Services;

/// <summary>
/// 主题服务 —— 实现 Windows 11 原生应用的"跟随系统浅色/深色主题"行为：
///   1. 读取注册表 AppsUseLightTheme 判断系统主题（也可由设置强制指定）；
///   2. 热切换 = 把 App 资源里的主题字典（Themes/Light.xaml 或 Dark.xaml）整体换掉，
///      所有控件样式都通过 DynamicResource 引用颜色，换字典即全局实时生效；
///   3. P/Invoke DwmSetWindowAttribute(DWMWA_USE_IMMERSIVE_DARK_MODE) 同步系统标题栏；
///   4. 切换后触发 <see cref="ThemeChanged"/>，界面据此重建带颜色缓存的行项目。
///
/// 性能说明（规范）：切换是纯事件驱动（用户改系统主题/设置时才发生），
/// 稳态零开销；字典中的画刷全部在 XAML 里 Freeze 冻结。
/// </summary>
public static class ThemeService
{
    /// <summary>当前是否深色主题（徽章等着色计算用）。</summary>
    public static bool IsDark { get; private set; }

    /// <summary>主题切换后触发（UI 线程外也可能触发，订阅方自行编组）。</summary>
    public static event Action? ThemeChanged;

    /// <summary>应用主题：按设置的模式（auto/light/dark）解析并切换整套资源。</summary>
    public static void Apply(AppSettings settings)
    {
        var dark = settings.ThemeMode switch
        {
            "dark" => true,
            "light" => false,
            _ => !ReadSystemUsesLightTheme(), // auto：跟随系统
        };

        if (Application.Current?.Resources is { } res)
        {
            var uri = new Uri($"pack://application:,,,/Themes/{(dark ? "Dark" : "Light")}.xaml", UriKind.Absolute);
            var dict = new ResourceDictionary { Source = uri };
            if (res.MergedDictionaries.Count == 0)
                res.MergedDictionaries.Add(dict);
            else
                res.MergedDictionaries[0] = dict; // 换首字典，DynamicResource 自动传播
        }

        IsDark = dark;
        UpdateAllWindowsTitleBar();
        try { ThemeChanged?.Invoke(); }
        catch (Exception ex) { DiagnosticsLog.Error("主题切换事件处理异常", ex); }
    }

    /// <summary>读取系统应用主题（浅色=true）。读不到时默认浅色。</summary>
    private static bool ReadSystemUsesLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key?.GetValue("AppsUseLightTheme") is int v) return v != 0;
        }
        catch { /* 注册表读取失败走默认 */ }
        return true;
    }

    // ---------- 标题栏深色（DWM） ----------

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>把窗口的系统标题栏切到当前主题（窗口 SourceInitialized 时调用一次即可）。</summary>
    public static void ApplyTitleBar(Window window)
    {
        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;
            int dark = IsDark ? 1 : 0;
            // 20 = DWMWA_USE_IMMERSIVE_DARK_MODE（Win10 2004+ / Win11）
            DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int));
        }
        catch { /* 旧系统不支持此属性，保持默认标题栏 */ }
    }

    /// <summary>更新当前所有可见窗口的标题栏（主题切换时调用）。</summary>
    private static void UpdateAllWindowsTitleBar()
    {
        if (Application.Current?.Windows is null) return;
        foreach (Window w in Application.Current.Windows)
        {
            if (w.IsLoaded) ApplyTitleBar(w);
        }
    }

    // ---------- 代码取色辅助（图表等代码绘制场景） ----------

    /// <summary>从当前主题取画刷，取不到回退灰色。</summary>
    public static Brush FindBrush(string key)
        => Application.Current?.TryFindResource(key) as Brush ?? Brushes.Gray;

    /// <summary>颜色向白色方向混合（深色主题下提亮文字用）。</summary>
    public static Color Lighten(Color c, float amount)
    {
        amount = Math.Clamp(amount, 0f, 1f);
        return Color.FromRgb(
            (byte)(c.R + (255 - c.R) * amount),
            (byte)(c.G + (255 - c.G) * amount),
            (byte)(c.B + (255 - c.B) * amount));
    }

    /// <summary>替换颜色的透明度（徽章底色用）。</summary>
    public static Color WithAlpha(Color c, byte alpha)
        => Color.FromArgb(alpha, c.R, c.G, c.B);
}
