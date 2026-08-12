using System.Threading.Channels;

namespace Gbar.Core.Logging;

/// <summary>
/// 线程安全静态日志门面：任意线程调用 Info/Debug/... 仅无锁入队，
/// 由单一后台消费线程串行写入所有已注册的 <see cref="ISink"/>。
/// 输出去向完全由宿主通过 <see cref="AddSink"/> 决定，Core 自身不做任何 Console/文件 IO。
/// </summary>
public static class Log
{
    private static readonly Channel<LogEntry> _queue =
        Channel.CreateUnbounded<LogEntry>(new UnboundedChannelOptions { SingleReader = true });

    private static readonly List<ISink> _sinks = new();
    private static readonly object _sinkLock = new();

    private static Thread? _consumer;
    private static volatile bool _running;

    /// <summary>最低输出级别，低于此级别的日志直接丢弃。默认 Info。</summary>
    public static LogLevel MinimumLevel { get; set; } = LogLevel.Info;

    /// <summary>注册输出目标，可在 Start 前后调用，重复注册同一实例无效。</summary>
    public static void AddSink(ISink sink)
    {
        lock (_sinkLock)
        {
            if (!_sinks.Contains(sink)) _sinks.Add(sink);
        }
    }

    /// <summary>启动后台消费线程。重复调用无效。</summary>
    public static void Start()
    {
        if (_running) return;
        _running = true;
        _consumer = new Thread(Consume)
        {
            Name = "Gbar.Log",
            IsBackground = true,
        };
        _consumer.Start();
    }

    /// <summary>停止接收新日志，排空队列并 flush/释放所有 Sink。必须在进程退出前调用。</summary>
    public static void Shutdown()
    {
        if (!_running) return;
        _running = false;
        _queue.Writer.Complete();
        _consumer?.Join();          // 等待队列排空，避免丢尾部日志
        _consumer = null;

        lock (_sinkLock)
        {
            foreach (var sink in _sinks)
            {
                sink.Flush();
                sink.Dispose();
            }
            _sinks.Clear();
        }
    }

    public static void Debug(string message) => Enqueue(LogLevel.Debug, message);
    public static void Info(string message) => Enqueue(LogLevel.Info, message);
    public static void Warn(string message) => Enqueue(LogLevel.Warn, message);
    public static void Error(string message) => Enqueue(LogLevel.Error, message);

    private static void Enqueue(LogLevel level, string message)
    {
        if (!_running || level < MinimumLevel) return;
        _queue.Writer.TryWrite(new LogEntry(level, message, DateTime.Now, Environment.CurrentManagedThreadId));
    }

    private static void Consume()
    {
        var reader = _queue.Reader;
        while (reader.WaitToReadAsync().AsTask().GetAwaiter().GetResult())
        {
            while (reader.TryRead(out var entry))
            {
                lock (_sinkLock)
                {
                    foreach (var sink in _sinks)
                    {
                        try { sink.Write(in entry); }
                        catch { /* 单个 sink 故障不影响其余 sink 与消费线程 */ }
                    }
                }
            }
        }
    }
}
