using System.IO;
using PCActivityLog.Models;
using PCActivityLog.Services;

namespace PCActivityLog.Watchers;

/// <summary>
/// 微信/QQ 文件监视模块 —— 监视聊天软件"已收到的文件"存储目录，记录每个落盘的新文件。
///
/// 原理：微信/QQ 收到文件后会自动保存到各自的存储目录（收到即落盘），
/// 监视这些目录的新文件出现，即为"收到文件"事件；事件产出时标记 localStatus=saved，
/// 由 <see cref="ImFileStatusService"/> 定期复核文件是否仍在（微信存储管理可能清理旧文件）。
///
/// 与 DownloadWatcher 的关系：落盘判定（大小稳定/TTL/去重）复用同一套已验证的模式；
/// 差异是递归监视（文件落在 年-月 子目录）、多账号各一个 FSW、来源标注、不记删除/重命名。
/// 有意不抽公共基类：下载路径是已回归验证的代码，保持其稳定；此处为独立实现。
///
/// 存储目录自动识别（含 OneDrive 文档重定向）：
///   微信 4.x：文档\xwechat_files\{账号}\msg\file\
///   微信 3.x：文档\WeChat Files\{账号}\FileStorage\File\
///   QQNT：  文档\Tencent Files\{QQ号}\nt_qq\nt_data\File\ 及旧版 FileRecv\
///   另支持 settings.ImFolders 自定义目录（来源标 im）。
/// </summary>
public class ImFileWatcher : IWatcherModule
{
    public string Id => "imfile";
    public string DisplayName => "微信/QQ 文件监视";

    private readonly AppSettings _settings;
    private readonly IEventSink _sink;
    private readonly object _lock = new();

    private readonly List<FileSystemWatcher> _watchers = new();
    private System.Threading.Timer? _scanTimer;

    /// <summary>待判定文件：路径 → (来源, 上次大小, 大小首次稳定时间, 首次见到时间)。</summary>
    private readonly Dictionary<string, (string Source, long Size, DateTime? StableSince, DateTime FirstSeen)> _pending = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>近期已记录（5 分钟去重，防同一文件多次触发）。</summary>
    private readonly Dictionary<string, DateTime> _recent = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>不记录的扩展名：浏览器/下载临时文件 + IM 目录里的系统噪音。</summary>
    private static readonly HashSet<string> SkipExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".crdownload", ".part", ".partial", ".tmp", ".download", ".opdownload", ".!ut", ".wdownload", ".!bc",
        ".db", ".db-shm", ".db-wal", // IM 本地数据库文件
        ".ini",                     // 配置文件
    };

    public ImFileWatcher(AppSettings settings, IEventSink sink)
    {
        _settings = settings;
        _sink = sink;
    }

    public bool IsRunning { get; private set; }

    // ================= 生命周期 =================

    public void Start()
    {
        Stop(); // 先自净：模块重启场景
        lock (_lock)
        {
            var found = 0;
            foreach (var (root, source) in DiscoverRoots())
            {
                try
                {
                    var fsw = new FileSystemWatcher(root)
                    {
                        IncludeSubdirectories = true, // 文件按 年-月 子目录存放
                        InternalBufferSize = 64 * 1024,
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite,
                    };
                    // 闭包捕获该目录对应的来源（wechat/qq/im）
                    var src = source;
                    fsw.Created += (_, e) => OnCreated(e.FullPath, src);
                    fsw.Error += (_, e) =>
                    {
                        DiagnosticsLog.Warn($"IM 文件监视 Error（自动重建）: {e.GetException()?.Message}");
                        try { if (IsRunning) Start(); } catch { /* 重建失败等下次重启 */ }
                    };
                    fsw.EnableRaisingEvents = true;
                    _watchers.Add(fsw);
                    found++;
                }
                catch (Exception ex)
                {
                    DiagnosticsLog.Warn($"IM 文件监视：无法监视 {root}: {ex.Message}");
                }
            }
            _scanTimer = new System.Threading.Timer(_ => SafeScan(), null, 1000, 1000);
            IsRunning = true;
            DiagnosticsLog.Info($"IM 文件监视已启动，识别到 {found} 个存储目录");
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            IsRunning = false;
            _scanTimer?.Dispose();
            _scanTimer = null;
            foreach (var w in _watchers)
            {
                w.EnableRaisingEvents = false;
                w.Dispose();
            }
            _watchers.Clear();
            _pending.Clear();
            _recent.Clear();
        }
    }

    public void Dispose() => Stop();

    // ================= 目录发现 =================

    /// <summary>扫描微信/QQ 的标准存储位置 + 用户自定义目录，返回 (根目录, 来源) 列表。</summary>
    private IEnumerable<(string Root, string Source)> DiscoverRoots()
    {
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (string.IsNullOrEmpty(docs)) yield break;

        if (_settings.ImWechat)
        {
            // 微信 4.x：xwechat_files\{wxid}\msg\file
            foreach (var root in SubDirs(Path.Combine(docs, "xwechat_files")))
                if (Directory.Exists(Path.Combine(root, "msg", "file")))
                    yield return (Path.Combine(root, "msg", "file"), "wechat");

            // 微信 3.x：WeChat Files\{wxid}\FileStorage\File
            foreach (var root in SubDirs(Path.Combine(docs, "WeChat Files")))
                if (Directory.Exists(Path.Combine(root, "FileStorage", "File")))
                    yield return (Path.Combine(root, "FileStorage", "File"), "wechat");
        }

        if (_settings.ImQq)
        {
            // QQNT：Tencent Files\{QQ号}\nt_qq\nt_data\File
            foreach (var root in SubDirs(Path.Combine(docs, "Tencent Files")))
            {
                if (Directory.Exists(Path.Combine(root, "nt_qq", "nt_data", "File")))
                    yield return (Path.Combine(root, "nt_qq", "nt_data", "File"), "qq");
                // 旧版 QQ：FileRecv
                if (Directory.Exists(Path.Combine(root, "FileRecv")))
                    yield return (Path.Combine(root, "FileRecv"), "qq");
            }
        }

        // 用户自定义目录
        foreach (var f in _settings.ImFolders)
            yield return (f, "im");
    }

    /// <summary>列出一层子目录（目录不存在返回空）。</summary>
    private static IEnumerable<string> SubDirs(string dir)
    {
        try { return Directory.Exists(dir) ? Directory.GetDirectories(dir) : Array.Empty<string>(); }
        catch { return Array.Empty<string>(); }
    }

    // ================= 事件与落盘判定（与 DownloadWatcher 相同的已验证模式） =================

    private void OnCreated(string fullPath, string source)
    {
        try
        {
            if (SkipExtensions.Contains(Path.GetExtension(fullPath))) return;
            lock (_lock) _pending[fullPath] = (source, GetSize(fullPath), null, DateTime.Now);
        }
        catch (Exception ex) { DiagnosticsLog.Error("IM 文件监视 OnCreated 异常", ex); }
    }

    private void SafeScan()
    {
        try
        {
            var settled = new List<(string Path, string Source)>();
            lock (_lock)
            {
                var now = DateTime.Now;
                foreach (var path in _pending.Keys.ToList())
                {
                    var st = _pending[path];

                    // TTL 驱逐：10 分钟未稳定（超大文件/异常）放弃跟踪
                    if (now - st.FirstSeen > TimeSpan.FromMinutes(10))
                    {
                        _pending.Remove(path);
                        continue;
                    }

                    var size = GetSize(path);
                    if (size < 0) { _pending.Remove(path); continue; }

                    if (size == st.Size)
                    {
                        if (st.StableSince is null)
                            _pending[path] = (st.Source, size, now, st.FirstSeen);
                        else if (now - st.StableSince.Value >= TimeSpan.FromSeconds(_settings.SettleSeconds))
                            settled.Add((path, st.Source));
                    }
                    else
                    {
                        _pending[path] = (st.Source, size, null, st.FirstSeen);
                    }
                }

                var expired = _recent.Where(kv => now - kv.Value > TimeSpan.FromMinutes(5)).ToList();
                foreach (var kv in expired) _recent.Remove(kv.Key);
            }

            foreach (var (path, source) in settled) Emit(path, source);
        }
        catch (Exception ex) { DiagnosticsLog.Error("IM 文件落盘扫描异常", ex); }
    }

    /// <summary>文件稳定落盘，产出"收到文件"事件（附保存状态）。</summary>
    private void Emit(string path, string source)
    {
        lock (_lock)
        {
            if (_recent.ContainsKey(path)) return;
            _recent[path] = DateTime.Now;
            _pending.Remove(path);
        }

        try
        {
            var fi = new FileInfo(path);
            if (!fi.Exists || fi.Length == 0) return;

            var e = new ActivityEvent
            {
                Type = EventType.ImFile,
                Name = fi.Name,
                Path = fi.FullName,
                SizeBytes = fi.Length,
                Source = source,
                OccurredAt = DateTime.Now,
            };
            e.SetExtra("localStatus", System.Text.Json.Nodes.JsonValue.Create("saved"));
            _sink.Dispatch(e);
        }
        catch (Exception ex) { DiagnosticsLog.Error($"产出 IM 文件事件失败 {path}", ex); }
    }

    /// <summary>取文件当前大小；不存在返回 -1。</summary>
    private static long GetSize(string path)
    {
        try { return File.Exists(path) ? new FileInfo(path).Length : -1; }
        catch { return -1; }
    }
}
