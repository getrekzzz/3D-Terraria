using System.Globalization;
using System.Text;

namespace Strata.Core.Commands;

/// <summary>Result of running a command.</summary>
public sealed record CommandResult(bool Ok, string Output)
{
    public static CommandResult Success(string output = "") => new(true, output);
    public static CommandResult Fail(string output) => new(false, output);
}

/// <summary>A console/script command.</summary>
public sealed class CommandDef
{
    public required string Name { get; init; }
    public required string Usage { get; init; }
    public required string Help { get; init; }
    public string[] Aliases { get; init; } = Array.Empty<string>();
    public required Func<CommandContext, CommandArgs, CommandResult> Run { get; init; }
}

/// <summary>Parsed arguments with typed accessors that produce readable errors.</summary>
public sealed class CommandArgs
{
    private readonly string[] _args;

    public CommandArgs(string[] args) => _args = args;

    public int Count => _args.Length;
    public string this[int i] => _args[i];
    public IReadOnlyList<string> All => _args;

    public string Str(int i, string name) => i < _args.Length ? _args[i] : throw new CommandException($"missing <{name}>");

    public string? OptStr(int i) => i < _args.Length ? _args[i] : null;

    public int Int(int i, string name) =>
        int.TryParse(Str(i, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : throw new CommandException($"<{name}> must be an integer, got '{_args[i]}'");

    public int OptInt(int i, int fallback) => i < _args.Length ? Int(i, "number") : fallback;

    public float Float(int i, string name) =>
        float.TryParse(Str(i, name), NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : throw new CommandException($"<{name}> must be a number, got '{_args[i]}'");

    public string Rest(int from) => string.Join(' ', _args.Skip(from));
}

/// <summary>Thrown by commands for user errors (reported as a failed result, never swallowed silently).</summary>
public sealed class CommandException : Exception
{
    public CommandException(string message) : base(message)
    {
    }
}

/// <summary>Registry and dispatcher shared by the in-game console and automated command scripts.</summary>
public sealed class CommandRegistry
{
    private readonly Dictionary<string, CommandDef> _byName = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<CommandDef> _commands = new();

    public IReadOnlyList<CommandDef> Commands => _commands;

    public void Register(CommandDef def)
    {
        foreach (var name in def.Aliases.Prepend(def.Name))
            if (!_byName.TryAdd(name, def)) throw new InvalidOperationException($"Command name '{name}' registered twice.");
        _commands.Add(def);
    }

    public CommandDef? Find(string name) => _byName.TryGetValue(name, out var d) ? d : null;

    /// <summary>Runs one line. Empty lines and '#' comments succeed with no output.</summary>
    public CommandResult Execute(CommandContext ctx, string line)
    {
        var tokens = Tokenize(line);
        if (tokens.Count == 0 || tokens[0].StartsWith('#')) return CommandResult.Success();
        var def = Find(tokens[0]);
        if (def == null) return CommandResult.Fail($"Unknown command '{tokens[0]}'. Try 'help'.");
        try
        {
            return def.Run(ctx, new CommandArgs(tokens.Skip(1).ToArray()));
        }
        catch (CommandException ex)
        {
            return CommandResult.Fail($"{def.Name}: {ex.Message}. Usage: {def.Usage}");
        }
    }

    /// <summary>Splits on whitespace, honouring double quotes.</summary>
    public static List<string> Tokenize(string line)
    {
        var result = new List<string>();
        var sb = new StringBuilder();
        bool quoted = false, any = false;
        foreach (char c in line.Trim())
        {
            if (c == '"')
            {
                quoted = !quoted;
                any = true;
            }
            else if (char.IsWhiteSpace(c) && !quoted)
            {
                if (any) result.Add(sb.ToString());
                sb.Clear();
                any = false;
            }
            else
            {
                sb.Append(c);
                any = true;
            }
        }
        if (any) result.Add(sb.ToString());
        return result;
    }

    public string HelpText(string? name = null)
    {
        if (name != null)
        {
            var d = Find(name) ?? throw new CommandException($"unknown command '{name}'");
            return $"{d.Usage}\n  {d.Help}" + (d.Aliases.Length > 0 ? $"\n  aliases: {string.Join(", ", d.Aliases)}" : "");
        }
        var sb = new StringBuilder();
        foreach (var d in _commands.OrderBy(c => c.Name, StringComparer.Ordinal)) sb.AppendLine($"{d.Usage,-38} {d.Help}");
        return sb.ToString().TrimEnd();
    }
}
