// 隔离验证：右键菜单 Command / CommandParameter 绑定是否解析（真实样式）
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;

var t = new Thread(() =>
{
    var app = new Application();
    var light = new ResourceDictionary { Source = new Uri("pack://application:,,,/PCActivityLog;component/Themes/Light.xaml") };
    var ctrl = new ResourceDictionary { Source = new Uri("pack://application:,,,/PCActivityLog;component/Themes/Controls.xaml") };

    var vm = new Vm();
    vm.Rows.Add(new Row()); vm.Rows.Add(new Row());

    var grid = new DataGrid { AutoGenerateColumns = false, Height = 260, Width = 640 };
    grid.Columns.Add(new DataGridTextColumn { Header = "时间", Binding = new Binding("Time"), Width = new DataGridLength(150) });
    grid.Columns.Add(new DataGridTextColumn { Header = "名称", Binding = new Binding("Name"), Width = new DataGridLength(200) });

    // 与主程序 MainWindow.xaml 完全一致的右键菜单结构
    var menu = new ContextMenu();
    var mi = new MenuItem { Header = "打开文件位置 / 网址" };
    mi.SetBinding(MenuItem.CommandProperty, new Binding("OpenLocationCommand"));
    mi.SetBinding(MenuItem.CommandParameterProperty,
        new Binding("PlacementTarget.SelectedItem") { RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(ContextMenu), 1) });
    menu.Items.Add(mi);
    grid.ContextMenu = menu;
    grid.ItemsSource = vm.Rows;
    grid.DataContext = vm;

    var win = new Window { Content = grid, Width = 700, Height = 340, Title = "ctxmenu-test" };
    win.Resources.MergedDictionaries.Add(light);
    win.Resources.MergedDictionaries.Add(ctrl);
    win.Show();
    for (int i = 0; i < 20; i++) DoEvents();

    // 选中第一行，程序化打开右键菜单（等价于用户右键）
    grid.SelectedItem = vm.Rows[0];
    grid.ContextMenu.PlacementTarget = grid;
    grid.ContextMenu.IsOpen = true;
    for (int i = 0; i < 20; i++) DoEvents();

    var beCmd = mi.GetBindingExpression(MenuItem.CommandProperty);
    var bePar = mi.GetBindingExpression(MenuItem.CommandParameterProperty);
    Console.WriteLine($"[绑定] Command 状态={beCmd?.Status} 值={(mi.Command != null ? "OK" : "null")}");
    Console.WriteLine($"[绑定] CommandParameter 状态={bePar?.Status} 值={(mi.CommandParameter?.GetType().Name ?? "null")}");
    Console.WriteLine($"[菜单] IsOpen={menu.IsOpen} MenuItem启用={mi.IsEnabled}");

    // 直接程序化调用命令（模拟点击菜单项）
    mi.Command?.Execute(mi.CommandParameter);

    menu.IsOpen = false;
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

public class Vm
{
    public System.Windows.Input.ICommand OpenLocationCommand { get; } = new RlyCmd(_ => Console.WriteLine("[CMD] OpenLocationCommand 被执行! 参数=" + _?.GetType().Name));
    public System.Collections.ObjectModel.ObservableCollection<Row> Rows { get; } = new();
}
public class Row { public string Time { get; set; } = "2026-09-04"; public string Name { get; set; } = "样本文件.txt"; }
public class RlyCmd : System.Windows.Input.ICommand
{
    private readonly Action<object?> _a;
    public RlyCmd(Action<object?> a) => _a = a;
    public event EventHandler? CanExecuteChanged { add { } remove { } }
    public bool CanExecute(object? p) { Console.WriteLine("[CMD] CanExecute 参数=" + (p?.GetType().Name ?? "null")); return true; }
    public void Execute(object? p) => _a(p);
}
