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

        // 响应式布局：窗口宽度不足时切单列，避免两列被挤压导致内容溢出
        SizeChanged += (_, e) => ApplyResponsiveLayout(e.NewSize.Width);
        Loaded += (_, _) => ApplyResponsiveLayout(ActualWidth);
    }

    /// <summary>是否已切为单列（避免重复调整）。</summary>
    private bool _singleColumn;

    /// <summary>
    /// 单列/双列自适应：可用宽度 &lt; 900 时改为单列（第二列收窄为 0，所有卡片移到第 0 列）。
    /// </summary>
    /// <summary>
    /// 单列/双列自适应：可用宽度 &lt; 900 时改为单列（右列折叠为 0，
    /// 右列内容整体移到左列下方，避免两列被挤压溢出）。
    /// </summary>
    private void ApplyResponsiveLayout(double width)
    {
        if (width <= 0) return;
        bool single = width < 900;
        if (single == _singleColumn) return;
        _singleColumn = single;

        if (single)
        {
            // 单列：右列宽度归零，把右列 StackPanel 挂到左列下方
            ColRight.Width = new GridLength(0);
            ColLeft.Width = new GridLength(1, GridUnitType.Star);
            if (RightColumn.Parent is Grid g && !ReferenceEquals(RightColumn.Parent, LeftColumn))
            {
                g.Children.Remove(RightColumn);
                LeftColumn.Children.Add(RightColumn);
            }
        }
        else
        {
            // 双列：右列恢复，把 StackPanel 放回第 1 列
            if (ReferenceEquals(RightColumn.Parent, LeftColumn))
            {
                LeftColumn.Children.Remove(RightColumn);
                Grid.SetColumn(RightColumn, 1);
                SettingsRoot.Children.Add(RightColumn);
            }
            ColLeft.Width = new GridLength(1, GridUnitType.Star);
            ColRight.Width = new GridLength(1, GridUnitType.Star);
        }
    }

    /// <summary>把配置读入界面控件（初始化期间不触发变更回调）。</summary>
    private void LoadFromSettings()
    {
        ThemeBox.SelectedIndex = _s.ThemeMode switch { "light" => 1, "dark" => 2, _ => 0 };

        FileModuleSwitch.IsOn = _s.ModuleFileEnabled;
        RecordDownloads.IsChecked = _s.RecordDownloads;
        RecordDeletes.IsChecked = _s.RecordDeletes;
        RecordRenames.IsChecked = _s.RecordRenames;
        SettleSeconds.Text = _s.SettleSeconds.ToString();
        RefreshFolderList();
        RefreshImFolderList();

        AppModuleSwitch.IsOn = _s.ModuleAppEnabled;
        RecordInstalls.IsChecked = _s.RecordInstalls;
        RecordUpdates.IsChecked = _s.RecordUpdates;
        RecordUninstalls.IsChecked = _s.RecordUninstalls;
        RegistryPollSeconds.Text = _s.RegistryPollSeconds.ToString();

        SystemModuleSwitch.IsOn = _s.ModuleSystemEnabled;

        ImModuleSwitch.IsOn = _s.ModuleImEnabled;
        ImWechat.IsChecked = _s.ImWechat;
        ImQq.IsChecked = _s.ImQq;


        NotificationsEnabled.IsOn = _s.NotificationsEnabled;
        NotifyDownloads.IsChecked = _s.NotifyDownloads;
        NotifyApp.IsChecked = _s.NotifyApp;
        NotifySystem.IsChecked = _s.NotifySystem;

        MinimizeToTray.IsOn = _s.MinimizeToTrayOnClose;
        StartWithWindows.IsOn = AutoStartService.IsEnabled();
        RegisterInSystem.IsOn = _s.RegisterInSystem;
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

    private void Register_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _s.RegisterInSystem = RegisterInSystem.IsOn; _s.Save();
        // 立即登记/清除（bin 目录开发运行时 Register 内部自动跳过）
        if (_s.RegisterInSystem) AppRegistrationService.Register();
        else AppRegistrationService.Unregister();
    }

    /// <summary>返回时间线（Frame 后退，保住时间线的筛选状态与滚动位置）。</summary>
    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (Frame.CanGoBack) Frame.GoBack();
        else Frame.Navigate(typeof(TimelinePage));
    }

    /// <summary>解析整数输入框；无效时返回默认值。</summary>
    private static int ParseInt(string text, int fallback)
        => int.TryParse(text?.Trim(), out var v) ? v : fallback;

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
            SaveAndRestartFileModule();
        }
    }

    private void RemoveFolder_Click(object sender, RoutedEventArgs e)
    {
        if (FolderList.SelectedItem is string path)
        {
            if (_s.WatchedFolders.Count <= 1)
            {
                ShowHint("至少保留一个监视文件夹");
                return;
            }
            _s.WatchedFolders.Remove(path);
            RefreshFolderList();
            SaveAndRestartFileModule();
        }
    }

    /// <summary>保存并重启文件监视模块（文件夹变更后立即生效）。</summary>
    private void SaveAndRestartFileModule()
    {
        _s.Save();
        if (_s.ModuleFileEnabled) App.Manager?.RestartModule("file");
        ShowHint("已生效 ✓");
    }

    /// <summary>在文件监视卡片下方显示一行操作反馈。</summary>
    private void ShowHint(string text)
    {
        if (HintText != null) HintText.Text = text;
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
            _s.Save();
            if (_s.ModuleImEnabled) App.Manager?.RestartModule("imfile"); // 立即生效
        }
    }

    /// <summary>删除选中的 IM 监视路径（立即生效）。</summary>
    private void RemoveImFolder_Click(object sender, RoutedEventArgs e)
    {
        if (ImFolderList.SelectedItem is string path)
        {
            _s.ImFolders.Remove(path);
            RefreshImFolderList();
            _s.Save();
            if (_s.ModuleImEnabled) App.Manager?.RestartModule("imfile");
        }
    }

    // ---------- 数值输入（失焦即保存生效） ----------

    /// <summary>
    /// 两个间隔输入框失焦时立即保存并重启对应模块（不再需要「应用更改」按钮）。
    /// 输入非法时回退到原值，并钳制到合法范围。
    /// </summary>
    private void Interval_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_loading) return;

        var newSettle = Math.Clamp(ParseInt(SettleSeconds.Text, _s.SettleSeconds), 2, 60);
        var newPoll = Math.Clamp(ParseInt(RegistryPollSeconds.Text, _s.RegistryPollSeconds), 10, 3600);

        bool changed = newSettle != _s.SettleSeconds || newPoll != _s.RegistryPollSeconds;
        _s.SettleSeconds = newSettle;
        _s.RegistryPollSeconds = newPoll;

        // 回显钳制后的值（用户输入越界时能看到实际生效值）
        SettleSeconds.Text = newSettle.ToString();
        RegistryPollSeconds.Text = newPoll.ToString();

        if (!changed) return;
        _s.Save();
        if (_s.ModuleFileEnabled) App.Manager?.RestartModule("file");
        if (_s.ModuleAppEnabled) App.Manager?.RestartModule("app");
        ShowHint("已生效 ✓");
    }
}
