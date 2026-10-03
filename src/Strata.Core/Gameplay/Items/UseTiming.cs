using Strata.Core.Content.Loading;
using Strata.Core.Presentation;

namespace Strata.Core.Gameplay.Items;

/// <summary>
/// Where in a use animation things happen, from the clip windows in data/config/use_styles.json. A swing's blade
/// waits out the wind-up, then sweeps its arc between the clip's strike start and end (<see cref="Sweep"/>); a thrust
/// (a rapier's jab or a spear's lunge, <see cref="ThrustClip"/>) connects only in its strike window
/// (<see cref="Striking"/>). Times are animation phases: 0 when the use starts, 1 when its (speed-scaled) animation ends.
/// </summary>
public sealed class UseTiming
{
    public const string Swing = "swing";
    public const string Mine = "mine";
    public const string Rapier = "rapier";
    public const string Spear = "spear";
    public const string Bow = "bow";

    /// <summary>How long past the strike end (phase) a strike still connects: the follow-through.</summary>
    public const float FollowThrough = 0.1f;

    private static readonly ClipTiming Whole = new() { Length = 1f, StrikeStart = 0f, StrikeEnd = 1f };

    public UseTiming(UseStyleConfig config) => Config = config;

    /// <summary>The shipped timing (<see cref="UseStyleConfig.Fallback"/>), used until the game loads the file.</summary>
    public static UseTiming Default { get; } = new(UseStyleConfig.Fallback());

    /// <summary>Loads and validates data/config/use_styles.json; invalid files throw with every problem listed.</summary>
    public static UseTiming Load(IDataSource data)
    {
        var config = UseStyleConfig.Load(data);
        var problems = config.Validate();
        if (problems.Count > 0) throw new InvalidDataException($"{UseStyleConfig.Path}: {string.Join("; ", problems)}");
        return new UseTiming(config);
    }

    public UseStyleConfig Config { get; }

    public float MaxClipRate => Config.MaxClipRate;

    private Entities.SwingCurve? _blade;

    /// <summary>The swing's blade curve (the swing style's bladeCurve): what the hitbox, trail, view model and body clip follow.</summary>
    public Entities.SwingCurve BladeCurve => _blade ??= Entities.SwingCurve.Blade(Config);

    /// <summary>A clip's timing; a clip without an entry strikes over its whole length.</summary>
    public ClipTiming Clip(string clip) => Config.Clips.TryGetValue(clip, out var timing) ? timing : Whole;

    /// <summary>
    /// The clip whose strike window times an item's thrust: its use style's clip (a rapier's jab, a spear's lunge) when
    /// that clip has a strike window, else the spear's.
    /// </summary>
    public string ThrustClip(ItemUseInfo info)
    {
        string clip = Config.ClipFor(info);
        return Config.Clips.TryGetValue(clip, out var c) && c.StrikeStart != null ? clip : Spear;
    }

    /// <summary>Phase at which the wind-up ends and the strike begins.</summary>
    public float StrikeStart(string clip) => Clip(clip).StrikeStart ?? 0f;

    /// <summary>
    /// How far the strike has travelled at phase <paramref name="t"/>: 0 through the wind-up, eased (smoothstep) to 1
    /// at the strike end, then 1 through the follow-through.
    /// </summary>
    public float Sweep(string clip, float t)
    {
        var c = Clip(clip);
        float start = c.StrikeStart ?? 0f, end = c.StrikeEnd ?? 1f;
        float s = Math.Clamp((t - start) / (end - start), 0f, 1f);
        return s * s * (3f - 2f * s);
    }

    /// <summary>The strike is live at phase <paramref name="t"/>: from the strike start to a little past its end.</summary>
    public bool Striking(string clip, float t)
    {
        var c = Clip(clip);
        return t >= (c.StrikeStart ?? 0f) && t <= (c.StrikeEnd ?? 1f) + FollowThrough;
    }
}
