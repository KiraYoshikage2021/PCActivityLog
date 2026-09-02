using PCActivityLog.Models;

namespace PCActivityLog.Watchers;

/// <summary>
/// 监视模块统一接口 —— 模块化架构的核心。
/// 每个记录功能（文件/软件/系统/浏览器）实现本接口，
/// 由 <see cref="WatcherManager"/> 统一注册、调度、运行中启停。
/// </summary>
/// <remarks>
/// 实现约定（资源生命周期铁律）：
/// 1. Start 必须可重入安全：内部先自净（释放旧资源再创建新的）；
/// 2. Stop/Dispose 必须释放本模块创建的所有 IDisposable（FSW、定时器、事件订阅）；
/// 3. 模块内所有事件回调用 try/catch 包裹，异常只记诊断日志，绝不上抛崩溃进程；
/// 4. 模块重开从当前时刻继续记录，不回补关闭期间的数据。
/// </remarks>
public interface IWatcherModule : IDisposable
{
    /// <summary>模块唯一标识（如 "file" / "app" / "system" / "browser"）。</summary>
    string Id { get; }

    /// <summary>模块中文显示名。</summary>
    string DisplayName { get; }

    /// <summary>当前是否处于运行状态。</summary>
    bool IsRunning { get; }

    /// <summary>启动模块（按当前配置创建资源并开始监视）。</summary>
    void Start();

    /// <summary>停止模块（释放全部资源；已入库数据不受影响）。</summary>
    void Stop();
}

/// <summary>
/// 事件出口 —— 模块产出事件的统一投递目标。
/// 由 WatcherManager 实现：投递到单写队列 → 触发气泡通知 → 安装事件触发关联匹配。
/// 模块只管调用 <see cref="Dispatch"/>，不关心后续处理。
/// </summary>
public interface IEventSink
{
    /// <summary>投递一条已确认的事件（调用即返回，不阻塞）。</summary>
    void Dispatch(ActivityEvent e);
}
