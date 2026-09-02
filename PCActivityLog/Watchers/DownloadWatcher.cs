using System.IO;
using PCActivityLog.Models;
using PCActivityLog.Services;

namespace PCActivityLog.Watchers;

/// <summary>
/// 文件监视模块 —— 监视配置的文件夹（默认"下载"文件夹），记录三类事件：
///   1. 下载完成：新文件出现且大小稳定（落盘判定）后记录，附 Zone.Identifier 来源网址；
///   2. 删除：监视范围内非临时文件被删除；
///   3. 重命名：旧名→新名（也覆盖"临时名→正式名"的浏览器下载完成场景）。
///
/// 三个子开关（RecordDownloads/RecordDeletes/RecordRenames）在事件产出时即时读取，
/// 因此在设置页切换子开关立即生效，无需重启模块。
///
/// 内存防护（规范第 4 条）：
///   - 落盘跟踪字典有 10 分钟 TTL，半截文件不会把字典撑大；
///   - 近期已记录下载有 5 分钟去重表，防 Created+Renamed 双触发；
///   - FileSystemWatcher 缓冲区溢出（Error 事件）时自动整体重建。
/// </summary>
public class DownloadWatcher : IWatcherModule
{
    public string Id => "file";
    public string DisplayName => "文件监视";

    private readonly AppSettings _settings;
    private readonly IEventSink _sink;
    private readonly object _lock = new();

    /// <summary>每个监视文件夹一个 FileSystemWatcher；Stop 时全部 Dispose。</summary>
    private readonly List<FileSystemWatcher> _watchers = new();

    /// <summary>落盘判定扫描定时器。</summary>
    private System.Threading.Timer? _scanTimer;

    /// <summary>待判定文件表：路径 → (上次大小, 大小首次稳定的时间, 首次见到的时间)。</summary>
    private readonly Dictionary<string, (long Size, DateTime? StableSince, DateTime FirstSeen)> _pending = new();

    /// <summary>近期已记录的下载（5 分钟去重）：路径 → 记录时间。</summary>
    private readonly Dictionary<string, DateTime> _recentDownloads = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>浏览器下载临时文件扩展名：这些文件不记录，等它们改名成正式文件再记。</summary>
    private static readonly HashSet<string> TempExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".crdownload", // Chrome / Edge
        ".part",       // Firefox
        ".partial",    // IE / 老版 Edge
        ".tmp",        // 通用临时文件
        ".download",   // 部分下载工具
        ".opdownload", // Opera
        ".!ut",        // uTorrent
        ".wdownload",  // 五花八门下载器
        ".!bc",        // BitComet
    };

    public DownloadWatcher(AppSettings settings, IEventSink sink)
    {
        _settings = settings;
        _sink = sink;
    }

    public bool IsRunning { get; private set; }

    // ================= 生命周期 =================

    public void Start()
    {
        Stop(); // 先自净：保证 Start 可安全重复调用（模块重启场景）
        lock (_lock)
        {
            foreach (var folder in _settings.WatchedFolders)
            {
                try
                {
                    var fsw = new FileSystemWatcher(folder)
                    {
                        IncludeSubdirectories = false,
                        InternalBufferSize = 64 * 1024, // 64KB，减少事件丢失
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite,
                    };
                    fsw.Created += OnCreated;
                    fsw.Renamed += OnRenamed;
                    fsw.Deleted += OnDeleted;
                    fsw.Error += OnError;
                    fsw.EnableRaisingEvents = true;
                    _watchers.Add(fsw);
                }
                catch (Exception ex)
                {
                    DiagnosticsLog.Warn($"文件监视：无法监视文件夹 {folder}: {ex.Message}");
                }
            }

            // 每秒扫描一次待判定文件表
            _scanTimer = new System.Threading.Timer(_ => SafeScan(), null, 1000, 1000);
            IsRunning = true;
            DiagnosticsLog.Info($"文件监视已启动，监视 { _watchers.Count } 个文件夹: {string.Join("; ", _settings.WatchedFolders)}");
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
                // 先摘事件再释放：保证配对解绑（规范第 3 条）
                w.Created -= OnCreated;
                w.Renamed -= OnRenamed;
                w.Deleted -= OnDeleted;
                w.Error -= OnError;
                w.EnableRaisingEvents = false;
                w.Dispose();
            }
            _watchers.Clear();
            _pending.Clear();
            _recentDownloads.Clear();
        }
    }

    public void Dispose() => Stop();

    // ================= 事件处理（FSW 回调在线程池线程，全部 try/catch） =================

    private void OnCreated(object sender, FileSystemEventArgs e)
    {
        try
        {
            if (IsTempFile(e.FullPath)) return; // 临时文件：等改名后的正式名
            lock (_lock) _pending[e.FullPath] = (GetSize(e.FullPath), null, DateTime.Now);
        }
        catch (Exception ex) { DiagnosticsLog.Error("文件监视 OnCreated 异常", ex); }
    }

    private void OnRenamed(object sender, RenamedEventArgs e)
    {
        try
        {
            lock (_lock) _pending.Remove(e.OldFullPath, out _);

            if (IsTempFile(e.FullPath)) return; // 改名成临时文件：忽略

            if (IsTempFile(e.OldFullPath))
            {
                // 浏览器下载完成的典型路径：xxx.crdownload → xxx.zip
                // 浏览器在改名时已写完文件，这里预置"已稳定"状态让下一轮扫描立刻产出
                lock (_lock) _pending[e.FullPath] =
                    (GetSize(e.FullPath), DateTime.Now - TimeSpan.FromSeconds(_settings.SettleSeconds),
                     DateTime.Now - TimeSpan.FromSeconds(_settings.SettleSeconds));
            }
            else if (_settings.RecordRenames)
            {
                // 普通改名（两个名字都不是临时文件）：记重命名事件
                EmitRename(e.OldFullPath, e.FullPath);
            }
        }
        catch (Exception ex) { DiagnosticsLog.Error("文件监视 OnRenamed 异常", ex); }
    }

    private void OnDeleted(object sender, FileSystemEventArgs e)
    {
        try
        {
            bool wasPending;
            lock (_lock)
            {
                wasPending = _pending.Remove(e.FullPath, out _);
                // 半截文件被删（下载取消）不产生任何记录
            }
            if (wasPending || IsTempFile(e.FullPath) || !_settings.RecordDeletes) return;

            _sink.Dispatch(new ActivityEvent
            {
                Type = EventType.FileDelete,
                Name = Path.GetFileName(e.FullPath),
                Path = e.FullPath,
                Source = "file",
                OccurredAt = DateTime.Now,
            });
        }
        catch (Exception ex) { DiagnosticsLog.Error("文件监视 OnDeleted 异常", ex); }
    }

    /// <summary>FileSystemWatcher 出错（通常是内部缓冲区溢出）→ 记日志并整体重建（规范第 6 条自愈）。</summary>
    private void OnError(object sender, ErrorEventArgs e)
    {
        DiagnosticsLog.Warn("文件监视 Error 事件（可能丢过事件），自动重建: " + (e.GetException()?.Message ?? "?"));
        try { if (IsRunning) Start(); }
        catch (Exception ex) { DiagnosticsLog.Error("文件监视重建失败", ex); }
    }

    // ================= 落盘判定 =================

    /// <summary>每秒扫描待判定表：大小稳定超时 → 记为下载；超 TTL → 驱逐。</summary>
    private void SafeScan()
    {
        try
        {
            var settled = new List<string>();
            lock (_lock)
            {
                var now = DateTime.Now;
                foreach (var path in _pending.Keys.ToList())
                {
                    var st = _pending[path];

                    // TTL 驱逐：10 分钟还没稳定（超大文件或异常状态）就放弃跟踪
                    if (now - st.FirstSeen > TimeSpan.FromMinutes(10))
                    {
                        _pending.Remove(path);
                        continue;
                    }

                    var size = GetSize(path);
                    if (size < 0) // 文件消失（删除事件还没到或被移动）
                    {
                        _pending.Remove(path);
                        continue;
                    }

                    if (size == st.Size)
                    {
                        // 大小没变：记录"从何时起稳定"
                        if (st.StableSince is null)
                            _pending[path] = (size, now, st.FirstSeen);
                        else if (now - st.StableSince.Value >= TimeSpan.FromSeconds(_settings.SettleSeconds))
                            settled.Add(path);
                    }
                    else
                    {
                        // 还在写入：刷新大小、重置稳定计时
                        _pending[path] = (size, null, st.FirstSeen);
                    }
                }

                // 去重表过期清理
                var expired = _recentDownloads.Where(kv => now - kv.Value > TimeSpan.FromMinutes(5)).ToList();
                foreach (var kv in expired) _recentDownloads.Remove(kv.Key);
            }

            foreach (var path in settled) EmitDownload(path);
        }
        catch (Exception ex) { DiagnosticsLog.Error("落盘扫描异常", ex); }
    }

    /// <summary>确认下载完成，读取来源网址并产出事件。</summary>
    private void EmitDownload(string path)
    {
        lock (_lock)
        {
            if (_recentDownloads.ContainsKey(path)) return; // 5 分钟内已记过（Created+Renamed 双触发）
            _recentDownloads[path] = DateTime.Now;
            _pending.Remove(path);
        }
        if (!_settings.RecordDownloads) return;

        try
        {
            var fi = new FileInfo(path);
            if (!fi.Exists || fi.Length == 0) return; // 空文件或已消失，不记

            var zone = ZoneIdentifierReader.Read(path);
            var e = new ActivityEvent
            {
                Type = EventType.Download,
                Name = fi.Name,
                Path = fi.FullName,
                SizeBytes = fi.Length,
                Version = fi.Extension.ToLowerInvariant(), // 复用 Version 列存扩展名
                Source = "file",
                OccurredAt = DateTime.Now,
                Url = zone?.HostUrl,
            };
            if (zone?.ReferrerUrl is { } refUrl) e.SetExtra("referrerUrl", System.Text.Json.Nodes.JsonValue.Create(refUrl));
            _sink.Dispatch(e);
        }
        catch (Exception ex) { DiagnosticsLog.Error($"产出下载事件失败 {path}", ex); }
    }

    /// <summary>产出重命名事件（旧路径存入 extra）。</summary>
    private void EmitRename(string oldPath, string newPath)
    {
        var e = new ActivityEvent
        {
            Type = EventType.FileRename,
            Name = Path.GetFileName(newPath),
            Path = newPath,
            Source = "file",
            OccurredAt = DateTime.Now,
        };
        e.SetExtra("renameOldPath", System.Text.Json.Nodes.JsonValue.Create(oldPath));
        _sink.Dispatch(e);
    }

    /// <summary>是否浏览器临时文件（按扩展名判断）。</summary>
    private static bool IsTempFile(string path)
        => TempExtensions.Contains(Path.GetExtension(path));

    /// <summary>取文件当前大小；不存在返回 -1。</summary>
    private static long GetSize(string path)
    {
        try { return File.Exists(path) ? new FileInfo(path).Length : -1; }
        catch { return -1; }
    }
}
