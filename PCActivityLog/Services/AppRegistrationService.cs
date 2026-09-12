using Microsoft.Win32;

namespace PCActivityLog.Services;

/// <summary>
/// 程序自我登记 —— 把程序信息写入 HKCU 卸载注册表
/// （Software\Microsoft\Windows\CurrentVersion\Uninstall\PCActivityLog）。
///
/// 目的：本程序是免安装的目录形式发布，注册表里没有任何身份信息，
/// Windows「设置 → 应用」与 BCUninstaller 等工具只能靠扫描目录"猜"，
/// 会把文件夹识别成 ".NET Runtime Crash Dump Generator" 之类的错误条目。
/// 登记后系统直接读注册表显示正确名称/版本/图标，并提供 --uninstall 卸载入口。
///
/// 仅写 HKCU，无需管理员权限；bin 目录下开发运行时不登记。
/// </summary>
public static class AppRegistrationService
{
    /// <summary>卸载键名（ASCII，仅作注册表键路径，界面显示用 DisplayName）。
    /// RegistryUninstallWatcher 依赖此常量排除自身，避免把自己的登记记成"安装"。</summary>
    public const string SelfKeyName = "PCActivityLog";

    public const string DisplayName = "电脑日志记录";

    private const string UninstallRoot = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";

    private static string KeyPath => UninstallRoot + "\\" + SelfKeyName;

    /// <summary>是否处于开发运行（exe 位于 bin 目录）——此时不登记，避免把调试路径写进系统。</summary>
    public static bool IsDevRun()
        => Environment.ProcessPath is not { } exe
           || exe.Contains(@"\bin\", StringComparison.OrdinalIgnoreCase);

    /// <summary>登记信息当前是否存在。</summary>
    public static bool IsRegistered()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: false);
            return key != null;
        }
        catch { return false; }
    }

    /// <summary>
    /// 当前安装是否由安装器（Inno Setup）管理。安装包用 AppId=PCActivityLog，
    /// 卸载条目键名为 <c>PCActivityLog_is1</c>；检测到它时程序跳过自我登记——
    /// 否则「设置 → 应用」会出现两条同名条目（安装器一条 + 自登记一条）。
    /// </summary>
    public static bool IsInstallerManaged()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(UninstallRoot + "\\" + SelfKeyName + "_is1", writable: false);
            return key != null;
        }
        catch { return false; }
    }

    /// <summary>
    /// 登记/更新程序信息。路径或版本变化时自动更新（程序搬位置后自愈），
    /// 无变化时不重复写注册表。静默失败只记日志。
    /// </summary>
    public static void Register()
    {
        var exe = Environment.ProcessPath;
        if (exe == null || IsDevRun()) return;

        // 安装器管理的安装：卸载条目由安装器负责，自我登记只会产生重复条目；
        // 若此前以绿色版方式运行登记过，顺带清掉旧条目
        if (IsInstallerManaged())
        {
            if (IsRegistered()) Unregister();
            return;
        }

        try
        {
            var dir = Path.GetDirectoryName(exe);
            if (string.IsNullOrEmpty(dir)) return;

            var version = GetVersion();
            using var key = Registry.CurrentUser.CreateSubKey(KeyPath);
            if (key is null) return;

            var existingLocation = key.GetValue("InstallLocation") as string;
            var existingVersion = key.GetValue("DisplayVersion") as string;
            var isNew = existingLocation is null;
            var locationChanged = !string.Equals(existingLocation, dir + "\\", StringComparison.OrdinalIgnoreCase);
            var versionChanged = !string.Equals(existingVersion, version, StringComparison.Ordinal);

            key.SetValue("DisplayName", DisplayName);
            key.SetValue("DisplayVersion", version);
            key.SetValue("Publisher", "PCActivityLog");
            key.SetValue("InstallLocation", dir + "\\");
            key.SetValue("DisplayIcon", exe);
            key.SetValue("UninstallString", "\"" + exe + "\" --uninstall");
            key.SetValue("NoModify", 1, RegistryValueKind.DWord);
            key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            if (isNew) key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
            // 目录大小估算要遍历全部文件（自包含发布数百 MB），只在首次登记/版本变化时算一次
            if (isNew || versionChanged)
                key.SetValue("EstimatedSize", ComputeSizeKb(dir), RegistryValueKind.DWord);

            if (isNew || locationChanged)
                DiagnosticsLog.Info($"已登记程序信息：{dir}（版本 {version}）");
        }
        catch (Exception ex)
        {
            DiagnosticsLog.Warn("登记程序信息失败: " + ex.Message);
        }
    }

    /// <summary>移除登记信息（卸载或用户关闭开关时）。键不存在时静默成功。</summary>
    public static void Unregister()
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(KeyPath, throwOnMissingSubKey: false);
        }
        catch (Exception ex)
        {
            DiagnosticsLog.Warn("移除程序登记失败: " + ex.Message);
        }
    }

    private static string GetVersion()
    {
        try
        {
            return System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
        }
        catch { return "0.0.0"; }
    }

    /// <summary>估算程序目录总大小（KB，注册表 EstimatedSize 的单位）。</summary>
    private static uint ComputeSizeKb(string dir)
    {
        try
        {
            long bytes = 0;
            foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                try { bytes += new FileInfo(f).Length; } catch { /* 单个文件读不到就跳过 */ }
            }
            return (uint)Math.Clamp(bytes / 1024, 0, int.MaxValue);
        }
        catch { return 0; }
    }
}
