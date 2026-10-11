using System.Text.Json;
using Strata.Core.Content;
using Strata.Core.Content.Loading;
using Strata.Core.World;

namespace Strata.Core.Presentation;

/// <summary>
/// Ambience (data/config/ambience.json): which looping sound beds and ambient particles play where. Each rule names
/// the biomes and layers it belongs to and the time of day; <see cref="Select{T}"/> picks the rules for a place. The
/// engine fades loops toward the chosen volumes and keeps particle emitters near the camera within the preset budget.
/// </summary>
public sealed class AmbienceConfig
{
    public const string Path = "data/config/ambience.json";

    /// <summary>Looping sound beds (assets/audio/ambience/&lt;loop&gt;.ogg).</summary>
    public List<AmbienceLoopRule> Loops { get; init; } = new();

    /// <summary>Ambient particle kinds near the camera (dust motes, spores, embers, leaves, fireflies, drips).</summary>
    public List<AmbienceParticleRule> Particles { get; init; } = new();

    /// <summary>Particle counts are multiplied by the graphics preset's scale (custom presets use medium).</summary>
    public Dictionary<string, float> ParticleScale { get; init; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["low"] = 0.35f, ["medium"] = 0.7f, ["high"] = 1f, ["ultra"] = 1.3f,
    };

    /// <summary>Cave acoustics: reverb on world sounds by enclosure and depth.</summary>
    public ReverbSettings Reverb { get; init; } = new();

    public static AmbienceConfig Load(IDataSource data) =>
        JsonSerializer.Deserialize<AmbienceConfig>(data.ReadText(Path), ContentJson.Options) ?? throw new JsonException($"{Path} is empty");

    public float ScaleFor(string preset) =>
        ParticleScale.TryGetValue(preset, out var s) ? s : ParticleScale.TryGetValue("medium", out var m) ? m : 0.7f;

    /// <summary>
    /// The rules of <paramref name="rules"/> that apply at a place, written into <paramref name="into"/> (cleared first;
    /// no allocation). A rule applies when one of its biomes is present (or it names none), its layer matches (or it
    /// names none) and its time matches. Exclusive rules (e.g. the underworld's) win: when one applies, only exclusive
    /// rules are kept.
    /// </summary>
    public static void Select<T>(List<T> rules, IReadOnlyCollection<string> biomes, WorldLayer layer, bool isDay, List<T> into) where T : AmbienceRule
    {
        into.Clear();
        bool exclusive = false;
        foreach (var r in rules)
        {
            if (!r.Matches(biomes, layer, isDay)) continue;
            if (r.Exclusive && !exclusive)
            {
                exclusive = true;
                into.Clear();
            }
            if (exclusive && !r.Exclusive) continue;
            into.Add(r);
        }
    }

    public List<string> Validate(ContentDatabase content)
    {
        var problems = new List<string>();
        foreach (var r in Loops.Cast<AmbienceRule>().Concat(Particles))
        {
            foreach (var b in r.Biomes)
                if (!content.Biomes.Contains(b)) problems.Add($"{r.Name}: unknown biome '{b}'");
            foreach (var l in r.Layers)
                if (!Enum.TryParse<WorldLayer>(l, ignoreCase: true, out _)) problems.Add($"{r.Name}: unknown layer '{l}'");
            if (r.Time is not (null or "day" or "night")) problems.Add($"{r.Name}: time must be day or night");
        }
        foreach (var l in Loops)
            if (l.Volume is <= 0f or > 1.5f) problems.Add($"{l.Loop}: volume must be within 0–1.5");
        foreach (var p in Particles)
            if (!AmbienceParticleRule.Kinds.Contains(p.Kind)) problems.Add($"particle kind '{p.Kind}' is not one of {string.Join(", ", AmbienceParticleRule.Kinds)}");
        return problems;
    }
}

/// <summary>Where an ambience rule applies.</summary>
public abstract class AmbienceRule
{
    /// <summary>Any of these biomes (empty: anywhere).</summary>
    public string[] Biomes { get; init; } = Array.Empty<string>();

    /// <summary>Any of these layers (empty: any layer).</summary>
    public string[] Layers { get; init; } = Array.Empty<string>();

    /// <summary>"day", "night" or null for both.</summary>
    public string? Time { get; init; }

    /// <summary>When it applies, rules that are not exclusive are dropped (the underworld silences the cave beds).</summary>
    public bool Exclusive { get; init; }

    public abstract string Name { get; }

    private WorldLayer[]? _layers;

    public bool Matches(IReadOnlyCollection<string> biomes, WorldLayer layer, bool isDay)
    {
        if (Time == "day" && !isDay || Time == "night" && isDay) return false;
        _layers ??= Array.ConvertAll(Layers, l => Enum.Parse<WorldLayer>(l, ignoreCase: true));
        if (_layers.Length > 0 && Array.IndexOf(_layers, layer) < 0) return false;
        if (Biomes.Length == 0) return true;
        foreach (var b in biomes)
            if (Array.IndexOf(Biomes, b) >= 0) return true;
        return false;
    }
}

public sealed class AmbienceLoopRule : AmbienceRule
{
    public string Loop { get; init; } = "";

    /// <summary>Linear loudness at full presence.</summary>
    public float Volume { get; init; } = 0.5f;

    /// <summary>Plays through the outdoors bus (muffled indoors) rather than as a cave bed.</summary>
    public bool Outdoors { get; init; }

    public override string Name => Loop;
}

public sealed class AmbienceParticleRule : AmbienceRule
{
    public static readonly string[] Kinds = { "dust", "spores", "embers", "leaves", "fireflies", "drips" };

    public string Kind { get; init; } = "dust";

    /// <summary>Particles alive at the high preset.</summary>
    public int Count { get; init; } = 100;

    public override string Name => Kind;
}

public sealed class ReverbSettings
{
    /// <summary>Reverb wet level fully enclosed near the surface, and deep underground.</summary>
    public float EnclosedWet { get; init; } = 0.18f;
    public float DeepWet { get; init; } = 0.32f;

    /// <summary>Depth (m) at which caves reach their deepest, largest sound.</summary>
    public float DeepMeters { get; init; } = 80f;

    public float RoomSize { get; init; } = 0.55f;
    public float DeepRoomSize { get; init; } = 0.85f;
}
