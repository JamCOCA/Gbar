namespace Gbar.Core.Logging;

/// <summary>
/// 级别过滤包装：低于指定级别的日志不写入内部 Sink，用于按输出目标分别控制级别。
/// 例如控制台只显示 Warn 以上，而文件保留完整日志。
/// </summary>
public sealed class LevelFilterSink : ISink
{
    private readonly ISink _inner;
    private readonly LogLevel _minimumLevel;

    public LevelFilterSink(ISink inner, LogLevel minimumLevel)
    {
        _inner = inner;
        _minimumLevel = minimumLevel;
    }

    public void Write(in LogEntry entry)
    {
        if (entry.Level >= _minimumLevel)
            _inner.Write(in entry);
    }

    public void Flush() => _inner.Flush();

    public void Dispose() => _inner.Dispose();
}
