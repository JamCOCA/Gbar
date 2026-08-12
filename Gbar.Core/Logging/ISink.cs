namespace Gbar.Core.Logging;

/// <summary>
/// 日志输出目标。所有 <see cref="Write"/> 调用都发生在 Log 的单一消费线程上，
/// 实现类无需考虑多线程并发，但必须保证自身写入高效（避免拖慢消费线程）。
/// </summary>
public interface ISink : IDisposable
{
    void Write(in LogEntry entry);

    /// <summary>将缓冲内容落盘/刷新，Shutdown 时调用。</summary>
    void Flush();
}
