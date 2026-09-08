using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PCActivityLog.Services;

namespace PCActivityLog.Views;

/// <summary>
/// 设置页 —— 模块开关卡片（与 WPF 版功能一致）。
/// 开关切换即时生效并持久化；文件夹列表与各类间隔点「应用更改」后重启对应模块生效。
/// </summary>
public sealed partial class SettingsPage : Page
{
    private readonly AppSettings _s;
    private bool _loading = true;

    public SettingsPage()
    {
        _s = App.Settings!;
        InitializeComponent();
        LoadFromSettings();
        _loading = false;
    }

    /// <summary>把配置读入界面控件（初始化期间不触发变更回调）。</summary>
    private void LoadFromSettings()
    {
        ThemeBox.SelectedIndex = _s.ThemeMode switch { "light" => 1, "dark" => 2, _ => 0 };

        FileModuleSwitch.IsOn = _s.ModuleFileEnabled;
        RecordDownloads.IsChecked = _s.RecordDownloads;
        RecordDeletes.IsChecked = _s.RecordDeletes;
        RecordRenames.IsChecked = _s.RecordRenames;
        SettleSeconds.Value = _s.SettleSeconds;
        RefreshFolderList();
        RefreshImFolderList();

        AppModuleSwitch.IsOn = _s.ModuleAppEnabled;
        RecordInstalls.IsChecked = _s.RecordInstalls;
        RecordUpdates.IsChecked = _s.RecordUpdates;
        RecordUninstalls.IsChecked = _s.RecordUninstalls;
        RegistryPollSeconds.Value = _s.RegistryPollSeconds;

        SystemModuleSwitch.IsOn = _s.ModuleSystemEnabled;

        ImModuleSwitch.IsOn = _s.ModuleImEnabled;
        ImWechat.IsChecked = _s.ImWechat;
        ImQq.IsChecked = _s.ImQq;


        NotificationsEnabled.IsOn = _s.NotificationsEnabled;
        NotifyDownloads.IsChecked = _s.NotifyDownloads;
        NotifyApp.IsChecked = _s.NotifyApp;
        NotifySystem.IsChecked = _s.NotifySystem;
        NotifyBrowse.IsChecked = _s.NotifyBrowse;

        MinimizeToTray.IsOn = _s.MinimizeToTrayOnClose;
        StartWithWindows.IsOn = AutoStartService.IsEnabled();
    }

    private void RefreshFolderList()
    {
        FolderList.ItemsSource = null;
        FolderList.ItemsSource = _s.WatchedFolders;
    }

    // ---------- 主题 ----------

    private void ThemeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        _s.ThemeMode = ThemeBox.SelectedIndex switch { 1 => "light", 2 => "dark", _ => "auto" };
        _s.Save();
        if (App.MainWindowInstance != null)
            ThemeService.Apply(_s, App.MainWindowInstance);
    }

    // ---------- 模块总开关（即时启停） ----------

    private void FileModule_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _s.ModuleFileEnabled = FileModuleSwitch.IsOn; _s.Save();
        App.Manager?.SetModuleEnabled("file", _s.ModuleFileEnabled);
    }

    private void AppModule_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _s.ModuleAppEnabled = AppModuleSwitch.IsOn; _s.Save();
        App.Manager?.SetModuleEnabled("app", _s.ModuleAppEnabled);
    }

    private void SystemModule_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _s.ModuleSystemEnabled = SystemModuleSwitch.IsOn; _s.Save();
        App.Manager?.SetModuleEnabled("system", _s.ModuleSystemEnabled);
    }

    private void ImModule_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _s.ModuleImEnabled = ImModuleSwitch.IsOn; _s.Save();
        App.Manager?.SetModuleEnabled("imfile", _s.ModuleImEnabled);
    }


    // ---------- 子开关（即时读取，无需重启） ----------

    private void SubSwitch_Click(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _s.RecordDownloads = RecordDownloads.IsChecked == true;
        _s.RecordDeletes = RecordDeletes.IsChecked == true;
        _s.RecordRenames = RecordRenames.IsChecked == true;
        _s.RecordInstalls = RecordInstalls.IsChecked == true;
        _s.RecordUpdates = RecordUpdates.IsChecked == true;
        _s.RecordUninstalls = RecordUninstalls.IsChecked == true;
        _s.ImWechat = ImWechat.IsChecked == true;
        _s.ImQq = ImQq.IsChecked == true;
        _s.NotifyDownloads = NotifyDownloads.IsChecked == true;
        _s.NotifyApp = NotifyApp.IsChecked == true;
        _s.NotifySystem = NotifySystem.IsChecked == true;
        _s.NotifyBrowse = NotifyBrowse.IsChecked == true;
        _s.Save();

        // 微信/QQ 子开关影响监视目录，需重启 IM 模块
        App.Manager?.RestartModule("imfile");
    }

    private void Notify_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _s.NotificationsEnabled = NotificationsEnabled.IsOn; _s.Save();
    }

    private void Minimize_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _s.MinimizeToTrayOnClose = MinimizeToTray.IsOn; _s.Save();
    }

    private void AutoStart_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        AutoStartService.SetEnabled(StartWithWindows.IsOn);
        _s.StartWithWindows = StartWithWindows.IsOn; _s.Save();
    }

    private void NumberBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        // 间隔类参数在「应用更改」时统一写回，这里不处理
    }

    // ---------- 文件夹管理 ----------

    private async void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        var picker = new Windows.Storage.Pickers.FolderPicker();
        picker.FileTypeFilter.Add("*");
        if (App.MainWindowInstance != null)
            WinRT.Interop.InitializeWithWindow.Initialize(picker,
                WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance));
        var folder = await picker.PickSingleFolderAsync();
        if (folder == null) return;
        if (!_s.WatchedFolders.Any(f => string.Equals(f, folder.Path, StringComparison.OrdinalIgnoreCase)))
        {
            _s.WatchedFolders.Add(folder.Path);
            RefreshFolderList();
        }
    }

    private void RemoveFolder_Click(object sender, RoutedEventArgs e)
    {
        if (FolderList.SelectedItem is string path)
        {
            _s.WatchedFolders.Remove(path);
            RefreshFolderList();
        }
    }

    // ---------- 微信/QQ 自定义路径管理 ----------

    /// <summary>刷新 IM 自定义路径列表。</summary>
    private void RefreshImFolderList()
    {
        ImFolderList.ItemsSource = null;
        ImFolderList.ItemsSource = _s.ImFolders;
    }

    /// <summary>添加 IM 监视路径（微信/QQ 改过默认存放位置时使用）。</summary>
    private async void AddImFolder_Click(object sender, RoutedEventArgs e)
    {
        var picker = new Windows.Storage.Pickers.FolderPicker();
        picker.FileTypeFilter.Add("*");
        if (App.MainWindowInstance != null)
            WinRT.Interop.InitializeWithWindow.Initialize(picker,
                WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance));
        var folder = await picker.PickSingleFolderAsync();
        if (folder == null) return;
        if (!_s.ImFolders.Any(f => string.Equals(f, folder.Path, StringComparison.OrdinalIgnoreCase)))
        {
            _s.ImFolders.Add(folder.Path);
            RefreshImFolderList();
        }
    }

    /// <summary>删除选中的 IM 监视路径。</summary>
    private void RemoveImFolder_Click(object sender, RoutedEventArgs e)
    {
        if (ImFolderList.SelectedItem is string path)
        {
            _s.ImFolders.Remove(path);
            RefreshImFolderList();
        }
    }

    // ---------- 应用更改 ----------

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (_s.WatchedFolders.Count == 0)
        {
            ApplyHint.Text = "至少保留一个监视文件夹";
            return;
        }

        _s.SettleSeconds = (int)Math.Clamp(SettleSeconds.Value, 2, 60);
        _s.RegistryPollSeconds = (int)Math.Clamp(RegistryPollSeconds.Value, 10, 3600);
        _s.Save();

        if (_s.ModuleFileEnabled) App.Manager?.RestartModule("file");
        if (_s.ModuleAppEnabled) App.Manager?.RestartModule("app");
        if (_s.ModuleImEnabled) App.Manager?.RestartModule("imfile"); // 自定义路径变更后重启 IM 模块

        ApplyHint.Text = "设置已应用 ✓";
    }
}
