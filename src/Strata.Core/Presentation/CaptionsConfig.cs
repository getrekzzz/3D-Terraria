using System.Text.Json;
using Strata.Core.Content;
using Strata.Core.Content.Loading;

namespace Strata.Core.Presentation;

/// <summary>
/// Subtitles for sounds (data/config/captions.json, X-12): which sound ids get a caption line above the chat log, its
/// words and kind (its colour), how far away a positional sound may be and still be captioned, and how long a line
/// stays after its sound. Sounds not listed are never captioned; the setting is off by default.
/// </summary>
public sealed class CaptionsConfig
{
    public const string Path = "data/config/captions.json";

    /// <summary>The kinds a caption may have; each has a colour in <see cref="Colors"/>.</summary>
    public static readonly string[] Kinds = { "world", "boss", "enemy", "town", "weather" };

    /// <summary>Seconds a line stays after its sound was last heard (a sound heard again refreshes it).</summary>
    public float Seconds { get; init; } = 3f;

    /// <summary>Seconds of the fade at the end of a line's life.</summary>
    public float FadeSeconds { get; init; } = 0.6f;

    /// <summary>Lines shown at most; a new caption replaces the line heard longest ago.</summary>
    public int MaxLines { get; init; } = 5;

    /// <summary>Metres from the listener within which a positional sound is captioned, unless the sound sets its own.</summary>
    public float Range { get; init; } = 32f;

    /// <summary>Text colour ("#rrggbb") per kind.</summary>
    public Dictionary<string, string> Colors { get; init; } = new();

    /// <summary>Captioned sounds by sound id (an sfx id, an ambience loop or a thunder clap).</summary>
    public Dictionary<string, CaptionDef> Sounds { get; init; } = new();

    public static CaptionsConfig Load(IDataSource data) =>
        JsonSerializer.Deserialize<CaptionsConfig>(data.ReadText(Path), ContentJson.Options) ?? throw new JsonException($"{Path} is empty");

    /// <summary>Problems with the file; <paramref name="soundExists"/> (when given) checks each sound id has audio.</summary>
    public List<string> Validate(Func<string, bool>? soundExists = null)
    {
        var problems = new List<string>();
        if (Seconds <= 0) problems.Add("seconds must be positive");
        if (FadeSeconds < 0 || FadeSeconds > Seconds) problems.Add("fadeSeconds must be within 0..seconds");
        if (MaxLines is < 1 or > 12) problems.Add("maxLines must be within 1..12");
        if (Range <= 0) problems.Add("range must be positive");
        foreach (var kind in Kinds)
            if (!Colors.TryGetValue(kind, out var hex) || !ColorVision.TryParseHex(hex, out _)) problems.Add($"colors: '{kind}' needs a #rrggbb colour");
        foreach (var (id, def) in Sounds)
        {
            if (string.IsNullOrWhiteSpace(def.Text)) problems.Add($"{id}: text is empty");
            if (Array.IndexOf(Kinds, def.Kind) < 0) problems.Add($"{id}: unknown kind '{def.Kind}' (expected {string.Join(", ", Kinds)})");
            if (def.Range is <= 0) problems.Add($"{id}: range must be positive");
            if (soundExists != null && !soundExists(id)) problems.Add($"{id}: no such sound");
        }
        return problems;
    }
}

/// <summary>One captioned sound: "Thunder cracks" (weather).</summary>
public sealed class CaptionDef
{
    public string Text { get; init; } = "";
    public string Kind { get; init; } = "world";

    /// <summary>Metres within which it is captioned when it has a position; null uses <see cref="CaptionsConfig.Range"/>.</summary>
    public float? Range { get; init; }
}
