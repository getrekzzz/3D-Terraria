using Godot;
using Strata.Core.Diagnostics;

namespace Strata.Game.Bootstrap;

/// <summary>Routes Core logging to Godot's output and a log file under user://logs; counts errors for tests.</summary>
public sealed class GodotLog : ILog
{
    private readonly System.IO.StreamWriter? _file;
    private readonly object _gate = new();
    private int _errors;
    private int _warnings;

    public GodotLog()
    {
        try
        {
            var dir = ProjectSettings.GlobalizePath("user://logs");
            System.IO.Directory.CreateDirectory(dir);
            _file = new System.IO.StreamWriter(System.IO.Path.Combine(dir, "strata.log"), append: false) { AutoFlush = true };
        }
        catch (System.Exception ex)
        {
            GD.PushWarning($"Log file unavailable: {ex.Message}");
        }
    }

    public LogLevel MinLevel { get; set; } = LogLevel.Info;
    public int Errors => _errors;
    public int Warnings => _warnings;

    public void Write(LogLevel level, string category, string message)
    {
        if (level == LogLevel.Error) System.Threading.Interlocked.Increment(ref _errors);
        if (level == LogLevel.Warning) System.Threading.Interlocked.Increment(ref _warnings);
        if (level < MinLevel) return;
        var line = $"[{level}] {category}: {message}";
        lock (_gate) _file?.WriteLine($"{System.DateTime.Now:HH:mm:ss.fff} {line}");
        switch (level)
        {
            case LogLevel.Error: GD.PushError(line); GD.PrintErr(line); break;
            case LogLevel.Warning: GD.PushWarning(line); GD.Print(line); break;
            default: GD.Print(line); break;
        }
    }
}
