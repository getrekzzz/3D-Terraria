namespace Strata.Tools.Commands;

public interface IToolCommand
{
    string Name { get; }
    string Description { get; }
    int Run(ToolContext context);
}

public sealed record ToolContext(string ProjectRoot, ToolOptions Options)
{
    public string PathOf(string relative) => Path.Combine(ProjectRoot, relative.Replace('/', Path.DirectorySeparatorChar));
}

/// <summary>Parses <c>--key value</c>, <c>--key=value</c> and <c>--flag</c> arguments.</summary>
public sealed class ToolOptions
{
    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

    public static ToolOptions Parse(IEnumerable<string> args)
    {
        var o = new ToolOptions();
        var list = args.ToList();
        for (int i = 0; i < list.Count; i++)
        {
            if (!list[i].StartsWith("--", StringComparison.Ordinal)) continue;
            var key = list[i][2..];
            int eq = key.IndexOf('=');
            if (eq > 0)
            {
                o._values[key[..eq]] = key[(eq + 1)..];
                continue;
            }
            bool hasValue = i + 1 < list.Count && !list[i + 1].StartsWith("--", StringComparison.Ordinal);
            o._values[key] = hasValue ? list[++i] : "true";
        }
        return o;
    }

    public string? Get(string key) => _values.TryGetValue(key, out var v) ? v : null;

    public bool Flag(string key) => Get(key) == "true";

    public int Int(string key, int fallback) => int.TryParse(Get(key), out var v) ? v : fallback;

    public long Long(string key, long fallback) => long.TryParse(Get(key), out var v) ? v : fallback;
}
