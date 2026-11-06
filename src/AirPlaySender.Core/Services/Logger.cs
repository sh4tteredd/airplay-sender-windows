using System.Globalization;

namespace AirPlaySender.Core.Services;

public enum LogLevel
{
    Info,
    Warning,
    Error
}

public sealed class Logger : IDisposable
{
    private readonly object _gate = new();
    private readonly StreamWriter? _writer;
    private readonly string _path;

    public static Logger Instance { get; } = CreateDefault();

    public event EventHandler<LogLineEventArgs>? LineWritten;

    private Logger(string path)
    {
        _path = path;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            _writer = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
            {
                AutoFlush = true
            };
        }
        catch
        {
            // Logging must never take the app down.
        }
    }

    public string LogPath => _path;

    private static Logger CreateDefault()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AirPlaySender",
            "logs");
        var path = Path.Combine(dir, $"airplay-sender-{DateTime.Now:yyyyMMdd}.log");
        return new Logger(path);
    }

    public void Info(string message) => Write(LogLevel.Info, message);
    public void Warn(string message) => Write(LogLevel.Warning, message);
    public void Error(string message) => Write(LogLevel.Error, message);

    public void Error(string message, Exception ex)
        => Write(LogLevel.Error, $"{message}: {ex.GetType().Name}: {ex.Message}");

    private void Write(LogLevel level, string message)
    {
        var line = $"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)} [{level.ToString().ToUpperInvariant()}] {message}";
        lock (_gate)
        {
            try { _writer?.WriteLine(line); } catch { }
        }
        LineWritten?.Invoke(this, new LogLineEventArgs(level, line));
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _writer?.Dispose();
        }
    }
}

public sealed class LogLineEventArgs : EventArgs
{
    public LogLineEventArgs(LogLevel level, string line)
    {
        Level = level;
        Line = line;
    }

    public LogLevel Level { get; }
    public string Line { get; }
}
