using PCActivityLog.Services;
using System.Windows;
using PCActivityLog.ViewModels;

namespace PCActivityLog.Views;

/// <summary>设置窗口 —— 模块开关卡片。开关切换即时生效；文件夹/间隔点「应用更改」。</summary>
public partial class SettingsWindow : Window
{
    public SettingsWindow(SettingsViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }

    /// <summary>标题栏跟随当前主题（浅/深）。</summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ThemeService.ApplyTitleBar(this);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
