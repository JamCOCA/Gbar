namespace Gbar.Core.Logging;

/// <summary>一条日志记录，由任意线程产生，经队列送到消费线程统一输出。</summary>
public readonly record struct LogEntry(
    LogLevel Level,
    string Message,
    DateTime Time,
    int ThreadId);
