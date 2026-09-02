using System.Threading.Channels;
using Microsoft.Data.Sqlite;
using PCActivityLog.Models;
using PCActivityLog.Services;

namespace PCActivityLog.Data;

/// <summary>
/// 单写队列 —— 所有事件的入库都必须经过这里。
/// 设计目的（内存/稳定性规范第 4 条）：
///   1. Channel 串行化所有数据库写入，避免多监视器并发写 SQLite 报 SQLITE_BUSY；
///   2. 监视器线程只做"投递即返回"，绝不阻塞在数据库上；
///   3. 消费端攒批 + 事务，减少磁盘 IO 与 WAL 膨胀；
///   4. 程序退出时 <see cref="FlushAsync"/> 限时冲写，保证不丢事件也不挂起。
/// </summary>
public class WriteQueue : IDisposable
{
    private readonly Channel<ActivityEvent> _channel = Channel.CreateUnbounded<ActivityEvent>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

    private readonly Database _db;
    private readonly Task _consumer;
    private readonly CancellationTokenSource _cts = new();

    /// <summary>一批事件成功入库后触发（UI 据此刷新列表，通知服务据此弹气泡）。
    /// 订阅方抛出的异常会被吞掉并记日志，绝不影响写入流程。</summary>
    public event EventHandler<IReadOnlyList<ActivityEvent>>? EventsCommitted;

    public WriteQueue(Database db)
    {
        _db = db;
        _consumer = Task.Run(() => ConsumeLoopAsync(_cts.Token));
    }

    /// <summary>投递一条事件（非阻塞，任意线程可调用）。</summary>
    public void Enqueue(ActivityEvent e) => _channel.Writer.TryWrite(e);

    /// <summary>消费循环：不断取批 → 事务写入 → 触发事件。</summary>
    private async Task ConsumeLoopAsync(CancellationToken ct)
    {
        var batch = new List<ActivityEvent>(200);
        try
        {
            while (!ct.IsCancellationRequested)
            {
                batch.Clear();
                // 阻塞等第一条（无事件时挂起，不耗 CPU）
                if (!await _channel.Reader.WaitToReadAsync(ct).ConfigureAwait(false)) break;
                while (batch.Count < 200 && _channel.Reader.TryRead(out var e))
                    batch.Add(e);

                // 再等 200ms 攒一批，把同一瞬间的大量事件合并成一个事务
                try
                {
                    await Task.Delay(200, ct).ConfigureAwait(false);
                    while (batch.Count < 200 && _channel.Reader.TryRead(out var e))
                        batch.Add(e);
                }
                catch (OperationCanceledException) { /* 退出路径，继续把已有批次写完 */ }

                if (batch.Count > 0)
                {
                    InsertBatch(batch);
                    RaiseCommitted(batch.ToArray());
                }
            }
        }
        catch (OperationCanceledException) { /* 正常退出 */ }
        catch (Exception ex)
        {
            DiagnosticsLog.Error("写入队列异常终止", ex);
        }
    }

    /// <summary>一个事务内插入一批事件，并回填每条的自增主键（事件关联器需要用 Id 反写 extra）。</summary>
    private void InsertBatch(List<ActivityEvent> batch)
    {
        try
        {
            using var conn = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = _db.DbPath,
                DefaultTimeout = 30,
                Pooling = true,
            }.ToString());
            conn.Open();
            using var tx = conn.BeginTransaction();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = Database.InsertSql;
            using var rowIdCmd = conn.CreateCommand();
            rowIdCmd.CommandText = "SELECT last_insert_rowid()";
            foreach (var e in batch)
            {
                Database.BindInsert(cmd, e, withId: false);
                cmd.ExecuteNonQuery();
                e.Id = (long)rowIdCmd.ExecuteScalar()!;
            }
            tx.Commit();
        }
        catch (Exception ex)
        {
            DiagnosticsLog.Error($"批量写入 {batch.Count} 条事件失败", ex);
        }
    }

    /// <summary>安全触发已提交事件（逐个调用订阅方，异常隔离）。</summary>
    private void RaiseCommitted(IReadOnlyList<ActivityEvent> events)
    {
        var handlers = EventsCommitted;
        if (handlers is null) return;
        foreach (EventHandler<IReadOnlyList<ActivityEvent>> h in handlers.GetInvocationList())
        {
            try { h(this, events); }
            catch (Exception ex) { DiagnosticsLog.Error("EventsCommitted 订阅方抛异常", ex); }
        }
    }

    /// <summary>退出前限时冲写剩余事件。最多等 3 秒，超时则放弃剩余（不阻塞关机）。</summary>
    public void FlushAsync()
    {
        try
        {
            _channel.Writer.TryComplete();
            _consumer.Wait(TimeSpan.FromSeconds(3));
        }
        catch (Exception ex)
        {
            DiagnosticsLog.Warn("冲写队列时异常: " + ex.Message);
        }
    }

    public void Dispose()
    {
        FlushAsync();
        _cts.Cancel();
        _cts.Dispose();
    }
}
