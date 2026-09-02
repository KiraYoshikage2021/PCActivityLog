using System.IO;

namespace PCActivityLog.Watchers;

/// <summary>
/// Zone.Identifier 读取器 —— 从文件的 NTFS 备用数据流中提取下载来源信息。
///
/// 原理：浏览器下载文件时，Windows 会自动在文件上附加一个名为
/// "Zone.Identifier" 的数据流（即 MotW，Mark of the Web），内容形如：
///   [ZoneTransfer]
///   ZoneId=3
///   ReferrerUrl=https://www.example.com/page
///   HostUrl=https://cdn.example.com/file.zip
/// 本类读取并解析出 HostUrl（下载直链）与 ReferrerUrl（引荐页面）。
/// 本地创建的文件没有这个流，返回 null —— 属正常情况。
/// </summary>
public static class ZoneIdentifierReader
{
    /// <summary>读取结果：下载直链 + 引荐页（任一可能为空）。</summary>
    public record ZoneInfo(string? HostUrl, string? ReferrerUrl);

    /// <summary>
    /// 读取指定文件的 Zone.Identifier 数据流。
    /// 文件可能尚被浏览器短暂占用，内部带一次 500ms 重试。
    /// 任何失败（无数据流/被占用/权限）都返回 null，绝不抛异常。
    /// </summary>
    public static ZoneInfo? Read(string filePath)
    {
        foreach (var delay in new[] { 0, 500 }) // 共尝试 2 次
        {
            if (delay > 0) Thread.Sleep(delay);
            try
            {
                // 备用数据流的访问方式：路径后加 ":Zone.Identifier"
                using var fs = new FileStream(filePath + ":Zone.Identifier", FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var sr = new StreamReader(fs);
                string? host = null, referrer = null;
                while (sr.ReadLine() is { } line)
                {
                    if (line.StartsWith("HostUrl=", StringComparison.OrdinalIgnoreCase))
                        host = line["HostUrl=".Length..].Trim();
                    else if (line.StartsWith("ReferrerUrl=", StringComparison.OrdinalIgnoreCase))
                        referrer = line["ReferrerUrl=".Length..].Trim();
                }
                return (host is null && referrer is null) ? null : new ZoneInfo(host, referrer);
            }
            catch (IOException) { /* 被占用 → 重试 */ }
            catch { return null; /* 无数据流或其他错误 → 不是网络下载的文件 */ }
        }
        return null;
    }
}
