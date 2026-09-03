using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using PCActivityLog.Services;
using PCActivityLog.Watchers;

namespace PCActivityLog.ViewModels;

/// <summary>
/// 设置页 ViewModel —— 模块开关卡片的数据与行为。
/// 生效策略：
///   · 模块总开关 / 通知开关 / 行为开关：切换立即生效并保存；
///   · 子开关（下载/删除/重命名等）：即时生效（监视器在事件产出时读取）；
///   · 文件夹列表与各类间隔：点「应用」按钮后重启对应模块生效。
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly AppSettings _s;
    private readonly WatcherManager _manager;

    /// <summary>监视文件夹列表（应用时写回设置并重启文件模块）。</summary>
    public ObservableCollection<string> Folders { get; } = new();

    // ---------- 模块总开关（立即启停模块） ----------
    [ObservableProperty] private bool moduleFileEnabled;
    [ObservableProperty] private bool moduleAppEnabled;
    [ObservableProperty] private bool moduleSystemEnabled;
    [ObservableProperty] private bool moduleBrowserEnabled;

    // ---------- 文件监视子开关（即时读取） ----------
    [ObservableProperty] private bool recordDownloads;
    [ObservableProperty] private bool recordDeletes;
    [ObservableProperty] private bool recordRenames;
    [ObservableProperty] private int settleSeconds;

    // ---------- 软件监视子开关 ----------
    [ObservableProperty] private bool recordInstalls;
    [ObservableProperty] private bool recordUpdates;
    [ObservableProperty] private bool recordUninstalls;
    [ObservableProperty] private int registryPollSeconds;

    // ---------- 浏览器子开关与参数 ----------
    [ObservableProperty] private bool browserChrome;
    [ObservableProperty] private bool browserEdge;
    [ObservableProperty] private bool browserFirefox;
    [ObservableProperty] private int browserPollMinutes;
    [ObservableProperty] private int browserRetentionDays;

    // ---------- 其他保留策略 ----------
    [ObservableProperty] private int otherRetentionDays;

    // ---------- 通知 ----------
    [ObservableProperty] private bool notificationsEnabled;
    [ObservableProperty] private bool notifyDownloads;
    [ObservableProperty] private bool notifyApp;
    [ObservableProperty] private bool notifySystem;
    [ObservableProperty] private bool notifyBrowse;

    // ---------- 微信/QQ 文件监视 ----------
    [ObservableProperty] private bool moduleImEnabled;
    [ObservableProperty] private bool imWechat;
    [ObservableProperty] private bool imQq;

    // ---------- 行为 ----------
    [ObservableProperty] private bool minimizeToTrayOnClose;
    [ObservableProperty] private bool startWithWindows;

    /// <summary>主题模式下拉框选中索引：0=跟随系统 1=浅色 2=深色。</summary>
    [ObservableProperty] private int themeModeIndex;

    public SettingsViewModel(AppSettings settings, WatcherManager manager)
    {
        _s = settings;
        _manager = manager;

        // 从配置读入
        ModuleFileEnabled = settings.ModuleFileEnabled;
        ModuleAppEnabled = settings.ModuleAppEnabled;
        ModuleSystemEnabled = settings.ModuleSystemEnabled;
        ModuleBrowserEnabled = settings.ModuleBrowserEnabled;

        RecordDownloads = settings.RecordDownloads;
        RecordDeletes = settings.RecordDeletes;
        RecordRenames = settings.RecordRenames;
        SettleSeconds = settings.SettleSeconds;

        RecordInstalls = settings.RecordInstalls;
        RecordUpdates = settings.RecordUpdates;
        RecordUninstalls = settings.RecordUninstalls;
        RegistryPollSeconds = settings.RegistryPollSeconds;

        BrowserChrome = settings.BrowserChrome;
        BrowserEdge = settings.BrowserEdge;
        BrowserFirefox = settings.BrowserFirefox;
        BrowserPollMinutes = settings.BrowserPollMinutes;
        BrowserRetentionDays = settings.BrowserRetentionDays;

        ModuleImEnabled = settings.ModuleImEnabled;
        ImWechat = settings.ImWechat;
        ImQq = settings.ImQq;

        OtherRetentionDays = settings.OtherRetentionDays;

        NotificationsEnabled = settings.NotificationsEnabled;
        NotifyDownloads = settings.NotifyDownloads;
        NotifyApp = settings.NotifyApp;
        NotifySystem = settings.NotifySystem;
        NotifyBrowse = settings.NotifyBrowse;

        MinimizeToTrayOnClose = settings.MinimizeToTrayOnClose;
        StartWithWindows = AutoStartService.IsEnabled();
        ThemeModeIndex = settings.ThemeMode switch
        {
            "light" => 1,
            "dark" => 2,
            _ => 0,
        };

        foreach (var f in settings.WatchedFolders) Folders.Add(f);
    }

    // ---------- 变更处理：立即生效类 ----------

    partial void OnModuleFileEnabledChanged(bool value)
    { _s.ModuleFileEnabled = value; _s.Save(); _manager.SetModuleEnabled("file", value); }
    partial void OnModuleAppEnabledChanged(bool value)
    { _s.ModuleAppEnabled = value; _s.Save(); _manager.SetModuleEnabled("app", value); }
    partial void OnModuleSystemEnabledChanged(bool value)
    { _s.ModuleSystemEnabled = value; _s.Save(); _manager.SetModuleEnabled("system", value); }
    partial void OnModuleBrowserEnabledChanged(bool value)
    { _s.ModuleBrowserEnabled = value; _s.Save(); _manager.SetModuleEnabled("browser", value); }

    partial void OnRecordDownloadsChanged(bool value) { _s.RecordDownloads = value; _s.Save(); }
    partial void OnRecordDeletesChanged(bool value) { _s.RecordDeletes = value; _s.Save(); }
    partial void OnRecordRenamesChanged(bool value) { _s.RecordRenames = value; _s.Save(); }
    partial void OnRecordInstallsChanged(bool value) { _s.RecordInstalls = value; _s.Save(); }
    partial void OnRecordUpdatesChanged(bool value) { _s.RecordUpdates = value; _s.Save(); }
    partial void OnRecordUninstallsChanged(bool value) { _s.RecordUninstalls = value; _s.Save(); }
    partial void OnBrowserChromeChanged(bool value) { _s.BrowserChrome = value; _s.Save(); }
    partial void OnBrowserEdgeChanged(bool value) { _s.BrowserEdge = value; _s.Save(); }
    partial void OnBrowserFirefoxChanged(bool value) { _s.BrowserFirefox = value; _s.Save(); }

    partial void OnModuleImEnabledChanged(bool value)
    { _s.ModuleImEnabled = value; _s.Save(); _manager.SetModuleEnabled("imfile", value); }
    partial void OnImWechatChanged(bool value) { _s.ImWechat = value; _s.Save(); _manager.RestartModule("imfile"); }
    partial void OnImQqChanged(bool value) { _s.ImQq = value; _s.Save(); _manager.RestartModule("imfile"); }

    partial void OnNotificationsEnabledChanged(bool value) { _s.NotificationsEnabled = value; _s.Save(); }
    partial void OnNotifyDownloadsChanged(bool value) { _s.NotifyDownloads = value; _s.Save(); }
    partial void OnNotifyAppChanged(bool value) { _s.NotifyApp = value; _s.Save(); }
    partial void OnNotifySystemChanged(bool value) { _s.NotifySystem = value; _s.Save(); }
    partial void OnNotifyBrowseChanged(bool value) { _s.NotifyBrowse = value; _s.Save(); }

    partial void OnMinimizeToTrayOnCloseChanged(bool value) { _s.MinimizeToTrayOnClose = value; _s.Save(); }
    partial void OnStartWithWindowsChanged(bool value) { AutoStartService.SetEnabled(value); }

    partial void OnThemeModeIndexChanged(int value)
    {
        _s.ThemeMode = value switch { 1 => "light", 2 => "dark", _ => "auto" };
        _s.Save();
        ThemeService.Apply(_s); // 立即热切换，无需重启
    }

    // ---------- 文件夹管理 ----------

    [RelayCommand]
    private void AddFolder()
    {
        var dlg = new OpenFolderDialog { Title = "选择要监视的文件夹" };
        if (dlg.ShowDialog() != true) return;
        if (!Folders.Any(f => string.Equals(f, dlg.FolderName, StringComparison.OrdinalIgnoreCase)))
            Folders.Add(dlg.FolderName);
    }

    /// <summary>当前在文件夹列表中选中的项（XAML 绑定用）。</summary>
    [ObservableProperty]
    private string? selectedFolder;

    [RelayCommand]
    private void RemoveFolder()
    {
        if (SelectedFolder != null) Folders.Remove(SelectedFolder);
    }

    // ---------- 应用（间隔/文件夹/保留期） ----------

    /// <summary>把本地编辑写回配置并重启相关模块。</summary>
    [RelayCommand]
    public void Apply()
    {
        if (Folders.Count == 0)
        {
            System.Windows.MessageBox.Show("至少保留一个监视文件夹。", "电脑日志记录",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }

        _s.WatchedFolders = Folders.ToList();
        _s.SettleSeconds = Math.Clamp(SettleSeconds, 2, 60);
        _s.RegistryPollSeconds = Math.Clamp(RegistryPollSeconds, 10, 3600);
        _s.BrowserPollMinutes = Math.Clamp(BrowserPollMinutes, 1, 120);
        _s.BrowserRetentionDays = Math.Max(0, BrowserRetentionDays);
        _s.OtherRetentionDays = Math.Max(0, OtherRetentionDays);

        // 数值框回显钳制后的值
        SettleSeconds = _s.SettleSeconds;
        RegistryPollSeconds = _s.RegistryPollSeconds;
        BrowserPollMinutes = _s.BrowserPollMinutes;

        _s.Save();

        // 重启模块让新参数生效（销毁旧资源 → 全新实例，规范第 2 条）
        if (_s.ModuleFileEnabled) _manager.RestartModule("file");
        if (_s.ModuleAppEnabled) _manager.RestartModule("app");
        if (_s.ModuleBrowserEnabled) _manager.RestartModule("browser");

        System.Windows.MessageBox.Show("设置已应用。", "电脑日志记录",
            System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
    }
}
