using System.Text.Json;
using Strata.Core.Content;
using Strata.Core.Content.Defs;
using Strata.Core.Content.Loading;
using Strata.Core.Gameplay.Items;

namespace Strata.Core.Presentation;

/// <summary>
/// How items are used on screen (data/config/use_styles.json). <see cref="Clips"/> holds the timing of each authored
/// body clip as fractions of the clip: the strike window of a swing, a mining chop or a thrust, and a bow's release
/// and re-draw. Gameplay reads it through <see cref="Gameplay.Items.UseTiming"/> (a swing only deals damage once its
/// wind-up is over), the engine poses the view model and trails by it, and the Blender clips are keyed to it.
/// <see cref="Styles"/> says how each <see cref="UseStyle"/> looks: the clip it plays and how far the arms follow the aim.
/// </summary>
public sealed class UseStyleConfig
{
    public const string Path = "data/config/use_styles.json";

    /// <summary>Clips that must be present with a strike window (gameplay strikes in it; the body clips are keyed to it).</summary>
    public static readonly string[] StrikeClips = { "swing", "mine", "rapier", "spear", "whip" };

    /// <summary>Clips that must be present with a release (the item leaves the hand).</summary>
    public static readonly string[] ReleaseClips = { "throw", "bow" };

    public const string BowClip = "bow";

    /// <summary>The fastest a clip may play relative to its authored length (1–5); quicker uses skip the wind-up.</summary>
    public float MaxClipRate { get; init; } = 2.6f;

    /// <summary>Timing per clip, keyed by the clip (animation) name.</summary>
    public Dictionary<string, ClipTiming> Clips { get; init; } = new();

    /// <summary>How each use style looks, keyed by its JSON name ("swing", "holdUp"); every style has an entry.</summary>
    public Dictionary<string, UseStyleEntry> Styles { get; init; } = new();

    private static readonly UseStyle[] AllStyles = Enum.GetValues<UseStyle>();
    private static readonly UseStyleEntry Plain = new();
    private UseStyleEntry[]? _byStyle;

    /// <summary>The JSON name of a style ("holdUp").</summary>
    public static string Key(UseStyle style) => JsonNamingPolicy.CamelCase.ConvertName(style.ToString());

    /// <summary>A style's entry (a plain "use" when the file has none for it).</summary>
    public UseStyleEntry Style(UseStyle style)
    {
        if (_byStyle == null)
        {
            var table = new UseStyleEntry[AllStyles.Length];
            foreach (var s in AllStyles) table[(int)s] = Styles.TryGetValue(Key(s), out var e) ? e : Plain;
            _byStyle = table;
        }
        return _byStyle[(int)style];
    }

    /// <summary>The upper-body clip an item's use plays: its style's clip, or the style's tool clip for tools.</summary>
    public string ClipFor(ItemUseInfo info)
    {
        var entry = Style(info.Style);
        return info.IsTool && entry.ToolClip != null ? entry.ToolClip : entry.Clip;
    }

    public static UseStyleConfig Load(IDataSource data) =>
        JsonSerializer.Deserialize<UseStyleConfig>(data.ReadText(Path), ContentJson.Options) ?? throw new JsonException($"{Path} is empty");

    /// <summary>The shipped values, for code that runs without the data file (tests, tools); a fresh copy each call.</summary>
    public static UseStyleConfig Fallback() => new()
    {
        MaxClipRate = 2.6f,
        Clips = new()
        {
            ["swing"] = new ClipTiming { Length = 0.5f, StrikeStart = 0.1f, StrikeEnd = 0.6f },
            ["mine"] = new ClipTiming { Length = 0.46f, StrikeStart = 0.18f, StrikeEnd = 0.32f },
            ["rapier"] = new ClipTiming { Length = 0.25f, StrikeStart = 0.05f, StrikeEnd = 0.15f },
            ["spear"] = new ClipTiming { Length = 0.4f, StrikeStart = 0.12f, StrikeEnd = 0.3f },
            ["whip"] = new ClipTiming { Length = 0.6f, StrikeStart = 0.35f, StrikeEnd = 0.55f },
            ["throw"] = new ClipTiming { Length = 0.45f, Release = 0.1f },
            ["bow"] = new ClipTiming { Length = 0.7f, Release = 0.06f, RedrawStart = 0.3f, RedrawEnd = 0.85f },
        },
        Styles = new()
        {
            ["none"] = new UseStyleEntry { Clip = "use", ArmAim = 0.5f },
            ["swing"] = new UseStyleEntry
            {
                Clip = "swing", ToolClip = "mine", ArmAim = 0.5f,
                BladeCurve = new CurveKey[] { new(0f, 158f, "lin"), new(0.1f, 166f, "out"), new(0.6f, 45f, "io"), new(0.72f, 26f, "out"), new(1f, 158f, "io") },
            },
            ["rapier"] = new UseStyleEntry { Clip = "rapier", ArmAim = 1f },
            ["spear"] = new UseStyleEntry { Clip = "spear", ArmAim = 1f },
            ["shoot"] = new UseStyleEntry { Clip = "gun", ArmAim = 1f },
            ["gun"] = new UseStyleEntry { Clip = "gun", ArmAim = 1f },
            ["bow"] = new UseStyleEntry { Clip = "bow", ArmAim = 1f },
            ["staff"] = new UseStyleEntry { Clip = "staff", ArmAim = 1f },
            ["tome"] = new UseStyleEntry { Clip = "tome", ArmAim = 1f },
            ["throw"] = new UseStyleEntry { Clip = "throw", ArmAim = 0.5f },
            ["yoyo"] = new UseStyleEntry { Clip = "yoyo", ArmAim = 1f },
            ["flail"] = new UseStyleEntry { Clip = "flail", ArmAim = 0.3f },
            ["whip"] = new UseStyleEntry { Clip = "whip", ArmAim = 0.5f },
            ["holdUp"] = new UseStyleEntry { Clip = "hold_up", ArmAim = 0.2f },
            ["drink"] = new UseStyleEntry { Clip = "drink", ArmAim = 0.5f },
            ["eat"] = new UseStyleEntry { Clip = "eat", ArmAim = 0.5f },
            ["place"] = new UseStyleEntry { Clip = "place", ArmAim = 0.5f },
        },
    };

    /// <summary>Problems with the file (missing clips or windows, values out of order or range); empty when valid.</summary>
    public List<string> Validate()
    {
        var problems = new List<string>();
        if (MaxClipRate is < 1f or > 5f) problems.Add("maxClipRate must be within 1–5");
        foreach (var name in StrikeClips)
            if (!Clips.TryGetValue(name, out var clip)) problems.Add($"clip '{name}' is missing");
            else if (clip.StrikeStart == null) problems.Add($"{name}: needs strikeStart and strikeEnd");
        foreach (var name in ReleaseClips)
            if (!Clips.TryGetValue(name, out var clip)) problems.Add($"clip '{name}' is missing");
            else if (clip.Release == null) problems.Add($"{name}: needs a release");
        if (Clips.TryGetValue(BowClip, out var bow) && bow.RedrawStart == null) problems.Add($"{BowClip}: needs redrawStart and redrawEnd");
        foreach (var (name, clip) in Clips)
        {
            if (name.Length == 0) problems.Add("a clip has an empty name");
            foreach (var problem in clip.Validate()) problems.Add($"{name}: {problem}");
        }
        foreach (var style in AllStyles)
            if (!Styles.ContainsKey(Key(style))) problems.Add($"style '{Key(style)}' is missing");
        foreach (var (name, entry) in Styles)
        {
            if (!Array.Exists(AllStyles, s => Key(s) == name)) problems.Add($"style '{name}' is not a use style");
            foreach (var problem in entry.Validate()) problems.Add($"style {name}: {problem}");
        }
        return problems;
    }
}

/// <summary>Timing of one authored clip; times are fractions of the clip (0 = start, 1 = end).</summary>
public sealed class ClipTiming
{
    /// <summary>Authored length in seconds (the engine stretches the clip to each use animation).</summary>
    public float Length { get; init; } = 0.5f;

    /// <summary>End of the wind-up: the strike begins.</summary>
    public float? StrikeStart { get; init; }

    /// <summary>The strike has swept its whole arc or reached full extension.</summary>
    public float? StrikeEnd { get; init; }

    /// <summary>The string snaps or the item leaves the hand.</summary>
    public float? Release { get; init; }

    /// <summary>A bow starts to nock and draw the next arrow.</summary>
    public float? RedrawStart { get; init; }

    /// <summary>A bow is fully drawn again.</summary>
    public float? RedrawEnd { get; init; }

    public IEnumerable<string> Validate()
    {
        if (Length is <= 0f or > 10f) yield return "length must be within (0, 10] seconds";
        if (StrikeStart.HasValue != StrikeEnd.HasValue) yield return "strikeStart and strikeEnd go together";
        else if (StrikeStart is { } start && StrikeEnd is { } end && !(start >= 0f && start < end && end <= 1f))
            yield return "needs 0 ≤ strikeStart < strikeEnd ≤ 1";
        if (Release is < 0f or > 1f) yield return "release must be within 0–1";
        if (RedrawStart.HasValue != RedrawEnd.HasValue) yield return "redrawStart and redrawEnd go together";
        else if (RedrawStart is { } from && RedrawEnd is { } to)
        {
            if (Release is not { } release) yield return "a re-draw needs a release";
            else if (!(release >= 0f && release < from && from < to && to <= 1f)) yield return "needs 0 ≤ release < redrawStart < redrawEnd ≤ 1";
        }
    }
}
