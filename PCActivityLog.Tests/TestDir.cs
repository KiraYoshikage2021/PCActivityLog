namespace PCActivityLog.Tests;

/// <summary>测试辅助：每个用例一个独立临时目录，互不干扰；结束时尽力清理。</summary>
internal static class TestDir
{
    public static string Create()
    {
        var dir = Path.Combine(Path.GetTempPath(), "PCActivityLogTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static void Cleanup(string dir)
    {
        try
        {
            // 先清连接池再删目录，否则 WAL/连接句柄会锁住文件
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        catch
        {
            // 临时目录删不掉只影响磁盘整洁，不影响测试结论
        }
    }
}
