using System.Numerics;

namespace Strata.Core.Presentation;

/// <summary>
/// The caption lines on show (X-12), Minecraft-style: a captioned sound adds a line, or refreshes the line already
/// showing the same words and moves it to the bottom (a flurry of hits reads as one line); a line fades out
/// <see cref="CaptionsConfig.Seconds"/> after its sound was last heard; when <see cref="CaptionsConfig.MaxLines"/>
/// are showing, the line heard longest ago gives way. A positional sound remembers where it was, so the engine turns its
/// arrow (<see cref="Bearing"/>) as the camera moves. Fixed storage: hearing and expiring allocate nothing.
/// </summary>
public sealed class CaptionFeed
{
    /// <summary>One caption: its words and kind (from the config), when its sound was last heard and where.</summary>
    public readonly record struct Line(string Text, string Kind, double Heard, bool Positioned, Vector3 Source);

    private readonly CaptionsConfig _config;
    private readonly Line[] _lines; // oldest heard first, newest last

    public CaptionFeed(CaptionsConfig config)
    {
        _config = config;
        _lines = new Line[Math.Max(1, config.MaxLines)];
    }

    public int Count { get; private set; }

    /// <summary>Changes whenever a line is added, refreshed, moved or removed (the engine rebinds its rows then).</summary>
    public int Version { get; private set; }

    public Line this[int index] => index >= 0 && index < Count ? _lines[index] : throw new ArgumentOutOfRangeException(nameof(index));

    public CaptionsConfig Config => _config;

    /// <summary>A sound without a position (the player's own, a jingle, thunder): captioned when listed.</summary>
    public bool Hear(string soundId, double now)
    {
        if (!_config.Sounds.TryGetValue(soundId, out var def)) return false;
        Put(def, now, false, default);
        return true;
    }

    /// <summary>A sound at <paramref name="source"/>: captioned when listed and within its range of <paramref name="listener"/>.</summary>
    public bool Hear(string soundId, double now, Vector3 source, Vector3 listener)
    {
        if (!_config.Sounds.TryGetValue(soundId, out var def)) return false;
        float range = def.Range ?? _config.Range;
        if (Vector3.DistanceSquared(source, listener) > range * range) return false;
        Put(def, now, true, source);
        return true;
    }

    /// <summary>Removes the lines whose time is up.</summary>
    public void Expire(double now)
    {
        int kept = 0;
        for (int i = 0; i < Count; i++)
        {
            if (now - _lines[i].Heard > _config.Seconds) continue;
            _lines[kept++] = _lines[i];
        }
        if (kept == Count) return;
        for (int i = kept; i < Count; i++) _lines[i] = default;
        Count = kept;
        Version++;
    }

    public void Clear()
    {
        if (Count == 0) return;
        Array.Clear(_lines);
        Count = 0;
        Version++;
    }

    /// <summary>Opacity of line <paramref name="index"/>: 1, then fading to 0 over the last <see cref="CaptionsConfig.FadeSeconds"/>.</summary>
    public float Alpha(int index, double now)
    {
        double left = _config.Seconds - (now - this[index].Heard);
        if (left >= _config.FadeSeconds || _config.FadeSeconds <= 0) return left > 0 ? 1f : 0f;
        return (float)Math.Clamp(left / _config.FadeSeconds, 0, 1);
    }

    /// <summary>
    /// Where <paramref name="source"/> lies from a listener at <paramref name="listener"/> looking along
    /// <paramref name="forward"/> (y up), in radians on the ground plane: 0 ahead, π/2 to the right, −π/2 to the left,
    /// ±π behind. Null when the sound is (nearly) straight above or below, or the listener looks straight up or down.
    /// </summary>
    public static float? Bearing(Vector3 listener, Vector3 forward, Vector3 source)
    {
        var d = source - listener;
        var ahead = new Vector2(forward.X, forward.Z);
        var to = new Vector2(d.X, d.Z);
        if (ahead.LengthSquared() < 1e-6f || to.LengthSquared() < 0.25f) return null;
        ahead = Vector2.Normalize(ahead);
        var right = new Vector2(-ahead.Y, ahead.X); // forward × up on the ground plane
        return MathF.Atan2(Vector2.Dot(to, right), Vector2.Dot(to, ahead));
    }

    private void Put(CaptionDef def, double now, bool positioned, Vector3 source)
    {
        int at = -1;
        for (int i = 0; i < Count; i++)
            if (ReferenceEquals(_lines[i].Text, def.Text) || _lines[i].Text == def.Text)
            {
                at = i;
                break;
            }
        if (at < 0 && Count == _lines.Length) at = 0; // full: the line heard longest ago gives way
        if (at >= 0)
        {
            for (int i = at; i < Count - 1; i++) _lines[i] = _lines[i + 1];
            Count--;
        }
        _lines[Count++] = new Line(def.Text, def.Kind, now, positioned, source);
        Version++;
    }
}
