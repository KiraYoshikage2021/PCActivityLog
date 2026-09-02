// 简易数据库查询工具 —— 仅用于开发验证，不属于正式软件。
// 用法: DbTool "SELECT ..." [db路径]   （DELETE/UPDATE/INSERT 自动以读写模式执行）
using Microsoft.Data.Sqlite;

var dbPath = args.Length > 1
    ? args[1]
    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PCActivityLog", "activity.db");

if (!File.Exists(dbPath)) { Console.WriteLine($"[db 不存在] {dbPath}"); return 2; }

var isWrite = args[0].TrimStart().StartsWith("DELETE", StringComparison.OrdinalIgnoreCase)
              || args[0].TrimStart().StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase)
              || args[0].TrimStart().StartsWith("INSERT", StringComparison.OrdinalIgnoreCase);

var conn = new SqliteConnection(new SqliteConnectionStringBuilder
{
    DataSource = dbPath,
    Mode = isWrite ? SqliteOpenMode.ReadWrite : SqliteOpenMode.ReadOnly,
    DefaultTimeout = 10,
}.ToString());
conn.Open();

using var cmd = conn.CreateCommand();
cmd.CommandText = args[0];

if (isWrite)
{
    var affected = cmd.ExecuteNonQuery();
    Console.WriteLine($"[写入完成，影响 {affected} 行]");
    return 0;
}

using var r = cmd.ExecuteReader();
var cols = new List<string>();
for (int i = 0; i < r.FieldCount; i++) cols.Add(r.GetName(i));
Console.WriteLine(string.Join(" | ", cols));
Console.WriteLine(new string('-', 60));

int rows = 0;
while (r.Read() && rows++ < 500)
{
    var vals = new List<string>();
    for (int i = 0; i < r.FieldCount; i++)
        vals.Add(r.IsDBNull(i) ? "" : r.GetValue(i).ToString());
    Console.WriteLine(string.Join(" | ", vals));
}
Console.WriteLine($"({rows} 行)");
return 0;
