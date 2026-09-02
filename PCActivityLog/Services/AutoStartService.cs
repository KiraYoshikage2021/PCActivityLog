using Microsoft.Win32;

namespace PCActivityLog.Services;

/// <summary>
/// 开机自启动服务 —— 写 HKCU\...\Run 注册表键（无需管理员权限）。
/// </summary>
public static class AutoStartService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "PCActivityLog";

    /// <summary>当前 exe 完整路径（带引号，兼容路径含空格）。</summary>
    private static string ExePath => "\"" + Environment.ProcessPath + "\"";

    /// <summary>查询开机自启动是否已启用。</summary>
    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
            return key?.GetValue(ValueName) is string;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>启用/禁用开机自启动。静默失败，只记日志。</summary>
    public static void SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (key is null) return;
            if (enabled) key.SetValue(ValueName, ExePath + " --minimized");
            else key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch (Exception ex)
        {
            DiagnosticsLog.Warn("设置开机自启动失败: " + ex.Message);
        }
    }

    /// <summary>若自启动已启用，把注册表里的路径刷新为当前 exe 路径（软件搬位置后自愈）。</summary>
    public static void RefreshPathIfEnabled()
    {
        try
        {
            if (IsEnabled()) SetEnabled(true);
        }
        catch { /* 忽略 */ }
    }
}
