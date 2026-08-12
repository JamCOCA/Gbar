using System.Text;

namespace Gbar.Core.Logging;

/// <summary>
/// txt 文件输出：按天滚动（logs/name-2026-08-12.txt），超过单文件上限自动切分序号。
/// 内部带写入缓冲，仅消费线程访问，无线程安全问题。
/// </summary>
public sealed class FileSink : ISink
{
    private readonly string _directory;
    private readonly string _baseName;
    private readonly long _maxFileBytes;

    private StreamWriter? _writer;
    private DateOnly _currentDate;
    private int _fileIndex;
    private long _currentBytes;

    /// <param name="directory">日志目录，不存在会自动创建。</param>
    /// <param name="baseName">日志文件基础名（不含扩展名）。</param>
    /// <param name="maxFileBytes">单文件上限，超过后切分为 name-date-N.txt，默认 32MB。</param>
    public FileSink(string directory, string baseName = "server", long maxFileBytes = 32 * 1024 * 1024)
    {
        _directory = directory;
        _baseName = baseName;
        _maxFileBytes = maxFileBytes;
    }

    public void Write(in LogEntry entry)
    {
        var date = DateOnly.FromDateTime(entry.Time);
        if (_writer is null || date != _currentDate)
            OpenNewFile(date);

        var line = Format(in entry);
        _writer!.WriteLine(line);
        _currentBytes += Encoding.UTF8.GetByteCount(line) + Environment.NewLine.Length;

        if (_currentBytes >= _maxFileBytes)
            OpenNewFile(date);  // 同一天超限，切分到下一个序号文件
    }

    public void Flush() => _writer?.Flush();

    public void Dispose() => _writer?.Dispose();

    private void OpenNewFile(DateOnly date)
    {
        _writer?.Flush();
        _writer?.Dispose();

        var sameDay = date == _currentDate;
        _currentDate = date;
        _fileIndex = sameDay ? _fileIndex + 1 : 0;   // 新一天从 0 开始
        _currentBytes = 0;

        Directory.CreateDirectory(_directory);
        var suffix = _fileIndex == 0 ? "" : $"-{_fileIndex}";
        var path = Path.Combine(_directory, $"{_baseName}-{date:yyyy-MM-dd}{suffix}.txt");
        _writer = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    private static string Format(in LogEntry e)
        => $"[{e.Time:HH:mm:ss.fff}] [{e.Level,-5}] [T{e.ThreadId}] {e.Message}";
}
