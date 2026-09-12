using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using PCActivityLog.Services;
using Windows.Graphics;

namespace PCActivityLog.Views;

/// <summary>
/// 卸载确认窗口（--uninstall 参数启动，注册表 UninstallString 也指向这里）。
/// 职责：清除系统登记信息与开机自启动；可选删除应用数据；打开资源管理器
/// 定位程序文件夹，程序文件由用户手动删除（进程无法可靠地删除自己，不做自动删除）。
/// 本窗口启动时 App 不初始化数据库/监视模块，因此数据目录可直接删除。
/// </summary>
public sealed partial class UninstallWindow : Window
{
    private bool _uninstalled;

    public UninstallWindow()
    {
        InitializeComponent();

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
        }
        AppWindow.Resize(new SizeInt32(560, 340));
        var icon = MainWindow.ResolveIconPath();
        if (icon != null) { try { AppWindow.SetIcon(icon); } catch { } }

        // 关闭窗口（任意途径）都走统一退出流程，释放互斥体等资源
        Closed += (_, _) => (App.Current as App)?.ExitApplication();
    }

    private void Uninstall_Click(object sender, RoutedEventArgs e)
    {
        if (_uninstalled)
        {
            (App.Current as App)?.ExitApplication(); // 按钮已变为「完成」，点击即退出
            return;
        }

        UninstallButton.IsEnabled = false;
        CancelButton.IsEnabled = false;
        DeleteDataCheck.IsEnabled = false;

        // 1. 清理注册表：注销程序登记 + 关闭开机自启动
        AutoStartService.SetEnabled(false);
        AppRegistrationService.Unregister();

        // 2. 可选：删除应用数据（日志数据库与设置）。本模式下未打开数据库，可直接删
        if (DeleteDataCheck.IsChecked == true)
        {
            try
            {
                var dataDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "PCActivityLog");
                if (Directory.Exists(dataDir)) Directory.Delete(dataDir, recursive: true);
            }
            catch (Exception ex)
            {
                AppendResult("应用数据删除失败（可能被其他程序占用），可稍后手动删除：%LOCALAPPDATA%\\PCActivityLog\n" + ex.Message);
            }
        }

        // 3. 打开资源管理器并选中程序 exe，文件夹由用户手动删除
        var exe = Environment.ProcessPath;
        if (exe != null)
        {
            try { System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + exe + "\""); }
            catch { /* 打开失败不影响卸载本身 */ }
        }

        AppendResult("✓ 已移除系统登记信息与开机自启动。请在打开的窗口中手动删除程序文件夹，然后点击「完成」退出。");
        _uninstalled = true;
        UninstallButton.Content = "完成";
        UninstallButton.IsEnabled = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
        => (App.Current as App)?.ExitApplication();

    private void AppendResult(string text)
    {
        ResultText.Text = ResultText.Text + text;
        ResultText.Visibility = Visibility.Visible;
    }
}
