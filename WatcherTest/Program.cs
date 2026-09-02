// 临时用途：生成应用图标 Assets/app.ico
var outPath = args.Length > 0
    ? args[0]
    : Path.Combine("..", "PCActivityLog", "Assets", "app.ico");
var t = new Thread(() =>
{
    PCActivityLog.Ui.TrayIconFactory.SaveIco(Path.GetFullPath(outPath));
    Console.WriteLine($"[host] ico 已生成: {Path.GetFullPath(outPath)}");
});
t.SetApartmentState(ApartmentState.STA);
t.Start();
t.Join();
