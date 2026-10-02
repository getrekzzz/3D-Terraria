using System.Text.Json;
using Strata.Core.Content;
using Strata.Core.Content.Loading;
using Strata.Core.Gameplay.Combat;

namespace Strata.Core.Presentation;

/// <summary>
/// Terraria's death messages (data/config/deaths.json): "Wanderer was slain by a Zombie.", "Wanderer fell to their
/// death.". Lines are kept per <see cref="HarmKind"/> (keys are the kind's camelCase name) with optional lines of their
/// own for a source id (a buff, a creature); the line is picked by the death count, so it varies yet never at random.
/// </summary>
public sealed class DeathMessages
{
    public const string Path = "data/config/deaths.json";

    public Dictionary<string, string[]> Lines { get; init; } = new();

    public Dictionary<string, string[]> Sources { get; init; } = new();

    /// <summary>Loads and checks the file; a kind without a line is an error.</summary>
    public static DeathMessages Load(IDataSource data)
    {
        var messages = JsonSerializer.Deserialize<DeathMessages>(data.ReadText(Path), ContentJson.Options) ?? throw new JsonException($"{Path} is empty");
        var problems = messages.Problems();
        if (problems.Count > 0) throw new JsonException($"{Path}: {string.Join("; ", problems)}");
        return messages;
    }

    /// <summary>What is wrong with the lines: a kind with none, or an empty line.</summary>
    public List<string> Problems()
    {
        var problems = new List<string>();
        foreach (var kind in Enum.GetValues<HarmKind>())
            if (!Lines.TryGetValue(Key(kind), out var lines) || lines.Length == 0) problems.Add($"no line for '{Key(kind)}'");
        foreach (var (key, lines) in Lines.Concat(Sources))
            if (lines.Any(string.IsNullOrWhiteSpace)) problems.Add($"an empty line in '{key}'");
        return problems;
    }

    /// <summary>
    /// The message for <paramref name="player"/> killed by <paramref name="harm"/>; <paramref name="variant"/> (the
    /// deaths before this one) picks among the lines.
    /// </summary>
    public string Format(string player, Harm harm, int variant)
    {
        if (harm.Id == null || !Sources.TryGetValue(harm.Id, out var lines) || lines.Length == 0)
            if (!Lines.TryGetValue(Key(harm.Kind), out lines) || lines.Length == 0)
                if (!Lines.TryGetValue(Key(HarmKind.Other), out lines) || lines.Length == 0)
                    lines = new[] { "{player} was slain." };
        string line = lines[((variant % lines.Length) + lines.Length) % lines.Length];
        // "On Fire!" reads "consumed by On Fire." mid-sentence: the line brings its own full stop.
        string source = harm.Source?.TrimEnd('!', '.') ?? "something";
        return line.Replace("{player}", player).Replace("{source}", source);
    }

    /// <summary>The file's key for a kind ("enemy", "drowning").</summary>
    public static string Key(HarmKind kind) => JsonNamingPolicy.CamelCase.ConvertName(kind.ToString());
}
