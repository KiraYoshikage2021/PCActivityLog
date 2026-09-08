using CommunityToolkit.WinUI.UI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using PCActivityLog.Models;
using PCActivityLog.Services;
using PCActivityLog.ViewModels;

namespace PCActivityLog.Views;

/// <summary>
/// 时间线页 —— DataGrid 事件列表 + 筛选/搜索/导出 + 右键菜单 + 列宽记忆。
/// 右键菜单在代码里取 Grid.SelectedItem（配合 Grid_RightTapped 的"右键即选中行"），
/// 避开 WPF 版踩过的绑定坑。
/// </summary>
public sealed partial class TimelinePage : Page
{
    public TimelineViewModel Vm { get; }

    public TimelinePage()
    {
        Vm = new TimelineViewModel(App.Db!, App.WriteQueueInstance!, new ExportService(App.Db!));
        App.TimelinePageVm = Vm; // 供退出时统一 Dispose（页面被缓存复用）
        InitializeComponent();

        Vm.ShowMessage = async (text, title) =>
        {
            var dlg = new ContentDialog
            {
                Title = title,
                Content = text,
                CloseButtonText = "确定",
                XamlRoot = XamlRoot,
            };
            await dlg.ShowAsync();
        };
        Vm.SelectRequested = OnSelectRequested;

        ApplyColumnWidths();

        // 页面被 Frame 缓存（CacheSize=3），Loaded 会多次触发：
        // 首次加载才查库，之后切回来只做轻量补刷（RefreshIfIdle 有 2 秒节流）。
        Loaded += (_, _) =>
        {
            if (!_initialized)
            {
                _initialized = true;
                Vm.Refresh();
            }
            else
            {
                Vm.RefreshIfIdle(); // 切回来时补刷新增事件，不重复全量查询
            }
        };

        // 列宽在页面离开时保存；不在这里 Dispose（缓存页面会复用，解绑事件会导致自动刷新失效）
        Unloaded += (_, _) => SaveColumnWidths();
    }

    /// <summary>是否已完成首次数据加载（避免每次切页都全量查库）。</summary>
    private bool _initialized;

    // ---------- 列宽记忆 ----------

    private void ApplyColumnWidths()
    {
        try
        {
            var saved = App.Settings?.ColumnWidths;
            if (saved is null || saved.Count == 0) return;
            foreach (var col in Grid.Columns)
            {
                if (col.Header is string header && saved.TryGetValue(header, out var w)
                    && w >= col.MinWidth && w is > 0 and < 5000)
                {
                    col.Width = new DataGridLength(w, DataGridLengthUnitType.Pixel);
                }
            }
        }
        catch (Exception ex) { DiagnosticsLog.Warn("应用列宽设置失败: " + ex.Message); }
    }

    /// <summary>把当前列宽写回设置（页面卸载/窗口关闭时调用）。</summary>
    public void SaveColumnWidths()
    {
        try
        {
            var settings = App.Settings;
            if (settings is null) return;
            foreach (var col in Grid.Columns)
            {
                if (col.Header is string header && col.ActualWidth >= 30)
                    settings.ColumnWidths[header] = Math.Round(col.ActualWidth);
            }
            settings.Save();
        }
        catch (Exception ex) { DiagnosticsLog.Warn("保存列宽失败: " + ex.Message); }
    }

    // ---------- 行交互 ----------

    private void OnSelectRequested(int index)
    {
        if (index < 0 || index >= Vm.Items.Count) return;
        Grid.SelectedIndex = index;
        Grid.ScrollIntoView(Vm.Items[index], null);
    }

    private async void Grid_DoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
        => await Vm.OpenOrJumpAsync(Grid.SelectedItem as ActivityEventItem);

    /// <summary>右键即选中所在行（默认右键不改选中行，否则菜单会操作旧选中行）。</summary>
    private void Grid_RightTapped(object sender, Microsoft.UI.Xaml.Input.RightTappedRoutedEventArgs e)
    {
        if (e.OriginalSource is DependencyObject d)
        {
            var row = FindAncestor<DataGridRow>(d);
            if (row?.DataContext is ActivityEventItem item) Grid.SelectedItem = item;
        }
    }

    private static T? FindAncestor<T>(DependencyObject o) where T : DependencyObject
    {
        while (o != null)
        {
            if (o is T t) return t;
            o = VisualTreeHelper.GetParent(o);
        }
        return null;
    }

    private async void OpenLocation_Click(object sender, RoutedEventArgs e)
        => await Vm.OpenLocationAsync(Grid.SelectedItem as ActivityEventItem);

    private void JumpLinked_Click(object sender, RoutedEventArgs e)
        => Vm.JumpLinked(Grid.SelectedItem as ActivityEventItem);

    private void CopyName_Click(object sender, RoutedEventArgs e)
        => Vm.CopyName(Grid.SelectedItem as ActivityEventItem);

    // ---------- 导出 ----------

    private async void ExportCsv_Click(object sender, RoutedEventArgs e) => await Vm.ExportCsvAsync();
    private async void ExportJson_Click(object sender, RoutedEventArgs e) => await Vm.ExportJsonAsync();

    // ---------- 手动添加记录 ----------

    private async void AddEvent_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AddEventDialog { XamlRoot = XamlRoot };
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary && dialog.Result != null)
            App.WriteQueueInstance?.Enqueue(dialog.Result);
    }
}
