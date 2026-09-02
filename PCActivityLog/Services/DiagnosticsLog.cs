using System.IO;

namespace PCActivityLog.Services;

/// <summary>
/// 滚动诊断日志 —— 记录模块启停、异常、警告，用于排查问题。
/// 文件大小有硬上限，避免日志本身变成磁盘泄漏源：
///   diag.log 超过 5 MB 时改名为 diag.old（覆盖旧备份），然后重新开始写。
/// 任何线程都可以直接调用静态方法，内部有锁保证线程安全。
/// </summary>
public static class DiagnosticsLog
{
    /// <summary>日志目录：%LOCALAPPDATA%\PCActivityLog\</summary>
    public static readonly string LogDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PCActivityLog");

    private static readonly string LogFile = Path.Combine(LogDir, "diag.log");
    private static readonly string OldFile = Path.Combine(LogDir, "diag.old");
    private static readonly object WriteLock = new();

    /// <summary>单文件大小上限（字节）。</summary>
    private const long MaxSize = 5 * 1024 * 1024;

    static DiagnosticsLog() => Directory.CreateDirectory(LogDir);

    /// <summary>记录信息级日志。</summary>
    public static void Info(string message) => Write("INFO ", message);

    /// <summary>记录警告日志。</summary>
    public static void Warn(string message) => Write("WARN ", message);

    /// <summary>记录错误日志（可附异常对象）。</summary>
    public static void Error(string message, Exception? ex = null)
        => Write("ERROR", ex is null ? message : $"{message} | {ex.GetType().Name}: {ex.Message}\r\n    {ex.StackTrace}");

    private static void Write(string level, string message)
    {
        try
        {
            lock (WriteLock)
            {
                // 超限时轮转：diag.log → diag.old
                if (File.Exists(LogFile) && new FileInfo(LogFile).Length > MaxSize)
                {
                    File.Delete(OldFile);
                    File.Move(LogFile, OldFile);
                }
                File.AppendAllText(LogFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}\r\n");
            }
        }
        catch
        {
            // 诊断日志自身绝不能抛异常影响主程序
        }
    }
}
