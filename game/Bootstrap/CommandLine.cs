using System.Collections.Generic;
using System.Globalization;
using Godot;

namespace Strata.Game.Bootstrap;

/// <summary>User arguments after "--" on the Godot command line, e.g. <c>-- --world=dev --seed=7 --smoke</c>.</summary>
public sealed class CommandLine
{
    private readonly Dictionary<string, string> _values = new();

    public CommandLine(IEnumerable<string> args)
    {
        foreach (var arg in args)
        {
            if (!arg.StartsWith("--")) continue;
            var body = arg.Substring(2);
            int eq = body.IndexOf('=');
            if (eq < 0) _values[body] = "true";
            else _values[body.Substring(0, eq)] = body.Substring(eq + 1);
        }
    }

    /// <summary>
    /// The OS arguments, plus launch arguments a smoke script declares on <c>#!</c> header lines (e.g.
    /// <c>#! --secret=dontdigup</c>, for a script that needs a special world); arguments on the command line win.
    /// </summary>
    public static CommandLine FromOs()
    {
        var line = new CommandLine(OS.GetCmdlineUserArgs());
        if (line.Get("smoke") is { } script && FileAccess.FileExists(script))
        {
            using var file = FileAccess.Open(script, FileAccess.ModeFlags.Read);
            var header = new List<string>();
            while (file != null && !file.EofReached())
            {
                string text = file.GetLine().Trim();
                if (text.StartsWith("#!")) header.AddRange(text.Substring(2).Split(' ', System.StringSplitOptions.RemoveEmptyEntries));
            }
            foreach (var (key, value) in new CommandLine(header)._values) line._values.TryAdd(key, value);
        }
        return line;
    }

    public string? Get(string key) => _values.TryGetValue(key, out var v) ? v : null;

    public bool Flag(string key) => Get(key) == "true";

    public long Long(string key, long fallback) =>
        long.TryParse(Get(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;
}
