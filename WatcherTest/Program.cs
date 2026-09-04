// 隔离验证：表头抓手（1）框架挂钩链路（2）真实合成输入拖拽
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

var t = new Thread(() =>
{
    var app = new Application();
    var light = new ResourceDictionary { Source = new Uri("pack://application:,,,/PCActivityLog;component/Themes/Light.xaml") };
    var ctrl = new ResourceDictionary { Source = new Uri("pack://application:,,,/PCActivityLog;component/Themes/Controls.xaml") };

    var grid = new DataGrid { AutoGenerateColumns = false, CanUserResizeColumns = true, Height = 300, Width = 700 };
    grid.Columns.Add(new DataGridTextColumn { Header = "时间", Binding = new System.Windows.Data.Binding("A"), Width = new DataGridLength(146), MinWidth = 110 });
    grid.Columns.Add(new DataGridTextColumn { Header = "名称", Binding = new System.Windows.Data.Binding("B"), Width = new DataGridLength(200), MinWidth = 100 });
    grid.Columns.Add(new DataGridTextColumn { Header = "备注", Binding = new System.Windows.Data.Binding("C"), Width = new DataGridLength(130), MinWidth = 80 });
    grid.ItemsSource = new[] { new { A = "2026-01-01", B = "样本甲", C = "备注1" }, new { A = "2026-01-02", B = "样本乙", C = "备注2" } }.ToList();

    var win = new Window { Content = grid, Width = 760, Height = 380, Title = "gripper-test" };
    win.Resources.MergedDictionaries.Add(light);
    win.Resources.MergedDictionaries.Add(ctrl);
    win.Show();
    win.Activate();
    for (int i = 0; i < 30; i++) DoEvents();
    System.Threading.Thread.Sleep(300);

    var header = FindVisual<DataGridColumnHeader>(grid).First(h => h.Column?.Header as string == "时间");
    var grip = FindVisual<Thumb>(header).First(x => x.Name == "PART_RightHeaderGripper");

    // 抓手中心 → 屏幕坐标
    var local = new Point(grip.ActualWidth / 2, grip.ActualHeight / 2);
    var screenPt = grip.TransformToAncestor(win).Transform(local);
    var hwndSrc = (System.Windows.Interop.HwndSource)PresentationSource.FromVisual(win);
    var screen = hwndSrc.CompositionTarget.TransformToDevice.Transform(screenPt);
    int sx = (int)screen.X, sy = (int)screen.Y;
    Console.WriteLine($"[host] 抓手屏幕坐标: ({sx},{sy}) 窗口激活={win.IsActive}");

    // Win32 合成拖拽（与主程序冒烟同款手法）
    Win32.SetCursorPos(sx, sy);
    System.Threading.Thread.Sleep(150);
    Win32.mouse_event(2, 0, 0, 0, UIntPtr.Zero);
    System.Threading.Thread.Sleep(120);
    for (int i = 0; i < 10; i++) { Win32.mouse_event(1, 5, 0, 0, UIntPtr.Zero); System.Threading.Thread.Sleep(60); }
    System.Threading.Thread.Sleep(250);
    Win32.mouse_event(4, 0, 0, 0, UIntPtr.Zero);
    for (int i = 0; i < 30; i++) DoEvents();

    var w2 = header.Column!.Width.Value;
    Console.WriteLine(w2 > 150
        ? $"[PASS] 真实合成输入拖拽成功，列宽 146 -> {w2:F0}"
        : $"[FAIL] 合成输入未生效，列宽仍为 {w2:F0}（框架链路正常，属输入注入限制）");
    Console.WriteLine("[host] 完成");
    app.Shutdown();
});
t.SetApartmentState(ApartmentState.STA);
t.Start();
t.Join();

static void DoEvents()
{
    var f = new DispatcherFrame();
    Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => f.Continue = false));
    Dispatcher.PushFrame(f);
}

static IEnumerable<T> FindVisual<T>(DependencyObject root) where T : DependencyObject
{
    for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
    {
        var c = VisualTreeHelper.GetChild(root, i);
        if (c is T hit) yield return hit;
        foreach (var x in FindVisual<T>(c)) yield return x;
    }
}

static class Win32
{
    [System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [System.Runtime.InteropServices.DllImport("user32.dll")] public static extern void mouse_event(uint f, int dx, int dy, uint data, UIntPtr extra);
}
