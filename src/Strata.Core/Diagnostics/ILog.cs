namespace Strata.Core.Diagnostics;

public enum LogLevel
{
    Debug,
    Info,
    Warning,
    Error,
}

/// <summary>Logging abstraction. Core never writes to the console directly.</summary>
public interface ILog
{
    void Write(LogLevel level, string category, string message);
}

public static class LogExtensions
{
    public static void Debug(this ILog log, string category, string message) => log.Write(LogLevel.Debug, category, message);
    public static void Info(this ILog log, string category, string message) => log.Write(LogLevel.Info, category, message);
    public static void Warn(this ILog log, string category, string message) => log.Write(LogLevel.Warning, category, message);
    public static void Error(this ILog log, string category, string message) => log.Write(LogLevel.Error, category, message);

    public static void Error(this ILog log, string category, string message, Exception ex) =>
        log.Write(LogLevel.Error, category, $"{message}: {ex}");
}

/// <summary>Writes to standard output/error. Used by tools and tests.</summary>
public sealed class ConsoleLog : ILog
{
    public LogLevel MinLevel { get; init; } = LogLevel.Info;

    public void Write(LogLevel level, string category, string message)
    {
        if (level < MinLevel) return;
        var line = $"[{level}] {category}: {message}";
        if (level >= LogLevel.Warning) Console.Error.WriteLine(line);
        else Console.WriteLine(line);
    }
}

/// <summary>Collects entries in memory (thread-safe). Used by tests and the smoke runner.</summary>
public sealed class MemoryLog : ILog
{
    private readonly object _gate = new();
    private readonly List<(LogLevel Level, string Category, string Message)> _entries = new();
    private readonly ILog? _forward;

    public MemoryLog(ILog? forward = null) => _forward = forward;

    public void Write(LogLevel level, string category, string message)
    {
        lock (_gate) _entries.Add((level, category, message));
        _forward?.Write(level, category, message);
    }

    public IReadOnlyList<(LogLevel Level, string Category, string Message)> Entries
    {
        get { lock (_gate) return _entries.ToArray(); }
    }

    public int Count(LogLevel level)
    {
        lock (_gate) return _entries.Count(e => e.Level == level);
    }
}

/// <summary>Discards everything.</summary>
public sealed class NullLog : ILog
{
    public static readonly NullLog Instance = new();

    public void Write(LogLevel level, string category, string message)
    {
    }
}
