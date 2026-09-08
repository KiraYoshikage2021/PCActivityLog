using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.Win32;
using System.Runtime.InteropServices;
using Windows.UI;

namespace PCActivityLog.Services;

/// <summary>
/// 主题服务（WinUI 3 版）—— 跟随 Windows 浅色/深色主题。
///
/// 与 WPF 版的差异：WinUI 没有 WPF 那种 MergedDictionaries 热替换机制，
/// 改用根元素 FrameworkElement.RequestedTheme 统一切换（ElementTheme），
/// 配合 App.xaml 里的 ThemeDictionaries 资源，深浅色自动生效。
/// 标题栏深色仍走 DWM（与 WPF 版同一套 P/Invoke）。
/// </summary>
public static class ThemeService
{
    /// <summary>当前是否深色主题（徽章等着色计算用）。</summary>
    public static bool IsDark { get; private set; }

    /// <summary>主题切换后触发（UI 线程），订阅方重建带颜色缓存的行项目。</summary>
    public static event Action? ThemeChanged;

    /// <summary>应用主题到指定窗口根元素。settings.ThemeMode: auto/light/dark。</summary>
    public static void Apply(AppSettings settings, Window? window = null)
    {
        IsDark = settings.ThemeMode switch
        {
            "dark" => true,
            "light" => false,
            _ => !ReadSystemUsesLightTheme(),
        };

        if (window?.Content is FrameworkElement root)
            root.RequestedTheme = IsDark ? ElementTheme.Dark : ElementTheme.Light;

        ApplyTitleBar(window);
        try { ThemeChanged?.Invoke(); }
        catch (Exception ex) { DiagnosticsLog.Error("主题切换事件处理异常", ex); }
    }

    /// <summary>读取系统应用主题（浅色=true）。</summary>
    private static bool ReadSystemUsesLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key?.GetValue("AppsUseLightTheme") is int v) return v != 0;
        }
        catch { }
        return true;
    }

    // ---------- 标题栏深色（DWM） ----------

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll")]
    private static extern IntPtr GetActiveWindow();

    /// <summary>把窗口标题栏切到当前主题。</summary>
    public static void ApplyTitleBar(Window? window)
    {
        try
        {
            IntPtr hwnd = window is null ? GetActiveWindow() : WinRT.Interop.WindowNative.GetWindowHandle(window);
            if (hwnd == IntPtr.Zero) return;
            int dark = IsDark ? 1 : 0;
            DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int)); // DWMWA_USE_IMMERSIVE_DARK_MODE
        }
        catch { /* 旧系统不支持，保持默认 */ }
    }

    // ---------- 代码取色辅助（图表等自绘场景） ----------

    /// <summary>从当前主题资源取画刷，取不到回退灰色。</summary>
    public static SolidColorBrush FindBrush(string key)
    {
        if (Application.Current?.Resources.TryGetValue(key, out var v) == true && v is SolidColorBrush b)
            return b;
        return new SolidColorBrush(Microsoft.UI.Colors.Gray);
    }

    /// <summary>颜色向白色方向混合（深色主题下提亮用）。</summary>
    public static Color Lighten(Color c, float amount)
    {
        amount = Math.Clamp(amount, 0f, 1f);
        return Color.FromArgb(c.A,
            (byte)(c.R + (255 - c.R) * amount),
            (byte)(c.G + (255 - c.G) * amount),
            (byte)(c.B + (255 - c.B) * amount));
    }

    /// <summary>替换颜色的透明度（徽章底色用）。</summary>
    public static Color WithAlpha(Color c, byte alpha) => Color.FromArgb(alpha, c.R, c.G, c.B);
}
