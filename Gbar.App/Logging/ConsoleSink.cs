using Gbar.Core.Logging;

namespace Gbar.App.Logging;

/// <summary>控制台输出，按级别着色。Console IO 只出现在 App 层。</summary>
public sealed class ConsoleSink : ISink
{
    public void Write(in LogEntry entry)
    {
        var previous = Console.ForegroundColor;
        Console.ForegroundColor = entry.Level switch
        {
            LogLevel.Debug => ConsoleColor.Gray,
            LogLevel.Info => ConsoleColor.White,
            LogLevel.Warn => ConsoleColor.Yellow,
            LogLevel.Error => ConsoleColor.Red,
            _ => previous,
        };
        Console.WriteLine($"[{entry.Time:HH:mm:ss.fff}] [{entry.Level,-5}] [T{entry.ThreadId}] {entry.Message}");
        Console.ForegroundColor = previous;
    }

    public void Flush() { }

    public void Dispose() { }
}
