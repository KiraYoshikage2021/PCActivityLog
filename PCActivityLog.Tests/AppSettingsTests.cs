using PCActivityLog.Services;
using Xunit;

namespace PCActivityLog.Tests;

/// <summary>
/// 配置读写测试：全部走 LoadFrom/SaveTo 指定临时路径，
/// 绝不触碰真实的 %LOCALAPPDATA%\PCActivityLog\settings.json。
/// </summary>
public class AppSettingsTests : IDisposable
{
    private readonly string _dir;
    private readonly string _file;

    public AppSettingsTests()
    {
        _dir = TestDir.Create();
        _file = Path.Combine(_dir, "settings.json");
    }

    public void Dispose() => TestDir.Cleanup(_dir);

    [Fact]
    public void SaveAndLoad_RoundTripsSettings()
    {
        var watchDir = Path.Combine(_dir, "watch"); // FixDefaults 只保留真实存在的目录
        Directory.CreateDirectory(watchDir);

        var s = new AppSettings
        {
            ModuleFileEnabled = false,
            WatchedFolders = { watchDir },
            SettleSeconds = 17,
            ThemeMode = "dark",
            OtherRetentionDays = 30,
            StartWithWindows = true,
        };
        s.SaveTo(_file);

        var loaded = AppSettings.LoadFrom(_file);
        Assert.False(loaded.ModuleFileEnabled);
        Assert.Equal(watchDir, Assert.Single(loaded.WatchedFolders));
        Assert.Equal(17, loaded.SettleSeconds);
        Assert.Equal("dark", loaded.ThemeMode);
        Assert.Equal(30, loaded.OtherRetentionDays);
        Assert.True(loaded.StartWithWindows);
    }

    [Fact]
    public void Load_IgnoresRemovedLegacyFields_KeepsRealOnes()
    {
        // v2.6.0 删除了浏览/列宽相关字段；旧配置文件里残留的键必须被安全忽略，
        // 而仍然存在的字段照常生效
        File.WriteAllText(_file, """
            {
              "ModuleFileEnabled": false,
              "ModuleBrowserEnabled": true,
              "BrowserChrome": false,
              "BrowserEdge": false,
              "BrowserFirefox": false,
              "BrowserPollMinutes": 3,
              "BrowserRetentionDays": 30,
              "NotifyBrowse": true,
              "ColumnWidths": { "时间": 100 },
              "OtherRetentionDays": 15
            }
            """);

        var s = AppSettings.LoadFrom(_file);
        Assert.False(s.ModuleFileEnabled);   // 仍存在的字段正常读取
        Assert.Equal(15, s.OtherRetentionDays);

        // 把加载结果另存一份：重新序列化不应再写出已删除的字段（确认死字段已清干净）
        var roundtrip = Path.Combine(_dir, "roundtrip.json");
        s.SaveTo(roundtrip);
        var json = File.ReadAllText(roundtrip);
        Assert.DoesNotContain("ModuleBrowserEnabled", json);
        Assert.DoesNotContain("ColumnWidths", json);
    }

    [Fact]
    public void Load_ClampsInvalidValues()
    {
        File.WriteAllText(_file, """
            { "SettleSeconds": 999, "RegistryPollSeconds": 1, "OtherRetentionDays": -5 }
            """);

        var s = AppSettings.LoadFrom(_file);
        Assert.Equal(60, s.SettleSeconds);        // 上限 60
        Assert.Equal(10, s.RegistryPollSeconds);  // 下限 10
        Assert.Equal(0, s.OtherRetentionDays);    // 非负
    }

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        var s = AppSettings.LoadFrom(Path.Combine(_dir, "not-exist.json"));
        Assert.True(s.ModuleFileEnabled);
        Assert.Equal("auto", s.ThemeMode);
        Assert.Equal(0, s.OtherRetentionDays);
    }

    [Fact]
    public void Load_CorruptFile_ReturnsDefaultsInsteadOfThrowing()
    {
        File.WriteAllText(_file, "{ 这不是合法 JSON ");
        var s = AppSettings.LoadFrom(_file);
        Assert.True(s.ModuleFileEnabled);
    }
}
