namespace PCActivityLog.Watchers;

/// <summary>
/// 近期名称过滤器 —— 用于"MSI 事件通道 与 注册表通道"的交叉去重。
/// MSI 事件先到（如安装成功），注册表轮询稍后发现同一个软件的新键，
/// 通过本过滤器在时间窗口内查到同名记录就跳过，避免同一事件记两条。
/// 线程安全。
/// </summary>
public class RecentNameFilter
{
    private readonly object _lock = new();
    private readonly List<(string Name, DateTime Time)> _items = new();

    /// <summary>登记一个名称（标准化：去空白、转小写）。</summary>
    public void Add(string name)
    {
        lock (_lock)
        {
            _items.Add((Normalize(name), DateTime.Now));
            Prune();
        }
    }

    /// <summary>查询窗口内是否登记过该名称。</summary>
    public bool ContainsRecent(string name, TimeSpan window)
    {
        var n = Normalize(name);
        var cutoff = DateTime.Now - window;
        lock (_lock)
        {
            Prune();
            return _items.Any(i => i.Name == n && i.Time >= cutoff);
        }
    }

    /// <summary>清空（模块停止时调用，防止残留状态）。</summary>
    public void Clear()
    {
        lock (_lock) _items.Clear();
    }

    /// <summary>删除超过 30 分钟的旧记录，防止列表无限增长。</summary>
    private void Prune()
    {
        var cutoff = DateTime.Now - TimeSpan.FromMinutes(30);
        _items.RemoveAll(i => i.Time < cutoff);
    }

    /// <summary>名称标准化：去首尾空白、压缩内部空白、忽略大小写。</summary>
    private static string Normalize(string s)
        => string.Join(' ', s.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
}
