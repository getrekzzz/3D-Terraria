using Strata.Core.Presentation;

namespace Strata.Core.Gameplay.Entities;

/// <summary>Easing into a curve key, named as in the clip timelines (tools/blender/bgen/human/motion.py <c>ease</c>).</summary>
public enum CurveEase
{
    /// <summary>Linear.</summary>
    Lin,

    /// <summary>Accelerating (cubic): a stab.</summary>
    In,

    /// <summary>Decelerating (cubic): into a hold.</summary>
    Out,

    /// <summary>Smoothstep: in and out.</summary>
    Io,
}

/// <summary>One key of a <see cref="SwingCurve"/>: the value at use phase <see cref="T"/>, eased in from the previous key.</summary>
public readonly record struct CurveKey(float T, float Value, CurveEase Ease);

/// <summary>
/// A value over a use's normalised time (0 when the use starts, 1 when its speed-scaled animation ends), keyed like the
/// authored body clips so the hitbox, the trail and the first-person arc all sample the same motion as the body.
/// <see cref="Blade"/> is the blade's angle (degrees) in the swing plane: 0 straight ahead, 90 straight up, over 90
/// behind the head, negative below the horizon in front. <see cref="Extension"/> is a thrust's extension (0 drawn in,
/// 1 at full reach). Both are built from the clip windows in data/config/use_styles.json, mirroring the keys of the
/// swing and thrust clips in tools/blender/bgen/human/actions.py. Pure and deterministic.
/// </summary>
public sealed class SwingCurve
{
    private readonly CurveKey[] _keys;

    public SwingCurve(IReadOnlyList<CurveKey> keys)
    {
        if (keys.Count == 0) throw new ArgumentException("a curve needs at least one key", nameof(keys));
        for (int i = 1; i < keys.Count; i++)
            if (keys[i].T < keys[i - 1].T) throw new ArgumentException("curve keys must be sorted by time", nameof(keys));
        _keys = keys.ToArray();
    }

    public IReadOnlyList<CurveKey> Keys => _keys;

    /// <summary>The value at phase <paramref name="t"/> (held at the first and last keys outside them).</summary>
    public float Evaluate(float t)
    {
        if (t <= _keys[0].T) return _keys[0].Value;
        for (int i = 1; i < _keys.Length; i++)
        {
            var b = _keys[i];
            if (t > b.T) continue;
            var a = _keys[i - 1];
            float u = (t - a.T) / MathF.Max(b.T - a.T, 1e-9f);
            return a.Value + (b.Value - a.Value) * Ease(b.Ease, u);
        }
        return _keys[^1].Value;
    }

    public static float Ease(CurveEase kind, float u) => kind switch
    {
        CurveEase.Lin => u,
        CurveEase.In => u * u * u,
        CurveEase.Out => 1f - (1f - u) * (1f - u) * (1f - u),
        _ => u * u * (3f - 2f * u),
    };

    // Blade angles at the swing clip's keys: raised behind the head, the wind-up's peak, struck low in front, the
    // follow-through, raised again (a chained swing carries on from there). The body clip's arm reaches the same
    // places at the same keys.
    public const float RaisedDeg = 150f, WindupDeg = 165f, StruckDeg = -45f, FollowDeg = -64f;

    /// <summary>The clip's follow-through end as a fraction of the rest of the use after the strike (actions.py).</summary>
    public const float FollowFraction = 0.3f;

    /// <summary>The blade-angle curve of a swing, keyed to the swing clip's strike window.</summary>
    public static SwingCurve Blade(ClipTiming clip)
    {
        float a = clip.StrikeStart ?? 0.1f, b = clip.StrikeEnd ?? 0.6f, f = b + FollowFraction * (1f - b);
        return new SwingCurve(new[]
        {
            new CurveKey(0f, RaisedDeg, CurveEase.Lin),
            new CurveKey(a, WindupDeg, CurveEase.Out),
            new CurveKey(b, StruckDeg, CurveEase.Io),
            new CurveKey(f, FollowDeg, CurveEase.Out),
            new CurveKey(1f, RaisedDeg, CurveEase.Io),
        });
    }

    /// <summary>
    /// The extension curve of a thrust, keyed to the thrust clip: drawn in through the wind-up, stabbed out to full
    /// reach by the strike end (accelerating), held briefly, then drawn back in.
    /// </summary>
    public static SwingCurve Extension(ClipTiming clip)
    {
        float a = clip.StrikeStart ?? 0.25f, b = clip.StrikeEnd ?? 0.5f, hold = MathF.Min(b + 0.1f, (b + 1f) / 2f);
        return new SwingCurve(new[]
        {
            new CurveKey(0f, 0f, CurveEase.Lin),
            new CurveKey(a, 0f, CurveEase.Out),
            new CurveKey(b, 1f, CurveEase.In),
            new CurveKey(hold, 1f, CurveEase.Lin),
            new CurveKey(1f, 0f, CurveEase.Io),
        });
    }

    /// <summary>The first phase at or after <paramref name="from"/> where the curve reaches <paramref name="value"/> (searched in fine steps), or 1.</summary>
    public float FirstReach(float value, float from = 0f)
    {
        const int steps = 2000;
        for (int i = 0; i <= steps; i++)
        {
            float t = from + (1f - from) * i / steps;
            if (Evaluate(t) >= value) return t;
        }
        return 1f;
    }
}
