namespace Strata.Core.Presentation;

/// <summary>Which title card is up: none, a boss awakening, or a boss defeated.</summary>
public enum BossCardKind
{
    None,
    Awoken,
    Defeated,
}

/// <summary>
/// The timeline of a boss's title card (<see cref="Content.Defs.BossIntro"/>), advanced in real seconds:
/// - Awoken: the card fades in over <see cref="FadeIn"/> while its title settles from <see cref="PopScale"/>, holds for
///   <see cref="Hold"/> and fades out over <see cref="FadeOut"/>. Cinema bars ease in and stay the intro's letterbox
///   seconds; the third-person camera pulls back by the intro's framing distance and is fully back within
///   <see cref="FramingSeconds"/> (it never steers the view: the player keeps control throughout).
/// - Defeated: the boss's name over "Defeated" for <see cref="DefeatHold"/>, shorter bars, and (when asked for) a
///   <see cref="DipSeconds"/> slow-motion dip in <see cref="TimeScale"/>.
/// <see cref="Skip"/> (the player attacks or is hurt) fades everything out over <see cref="SkipFade"/>, never before the
/// card has shown for <see cref="MinShown"/>. A new card replaces the one showing. Allocation-free; the game reads the
/// properties every frame.
/// </summary>
public sealed class BossIntroModel
{
    public const float FadeIn = 0.35f;
    public const float Hold = 2.4f;
    public const float DefeatHold = 2.0f;
    public const float FadeOut = 0.8f;
    public const float PopScale = 1.12f;

    /// <summary>A skip fades the card, bars and framing out over this long.</summary>
    public const float SkipFade = 0.3f;

    /// <summary>A skip asked for earlier waits until the card has shown this long.</summary>
    public const float MinShown = 0.5f;

    /// <summary>The camera framing eases in over <see cref="FramingIn"/>, and is back to normal by <see cref="FramingSeconds"/>.</summary>
    public const float FramingIn = 0.45f, FramingOut = 0.6f, FramingSeconds = 1.5f;

    public const float MaxFramingDistance = 6f;

    public const float BarsIn = 0.3f, BarsOut = 0.45f;

    /// <summary>The longest letterbox an intro may ask for (it must end before the card does).</summary>
    public const float MaxLetterbox = 2.5f;

    /// <summary>The defeat card's bars stay at most this long.</summary>
    public const float DefeatLetterbox = 1.2f;

    /// <summary>The slow-motion dip on a defeat: <see cref="DipScale"/> for <see cref="DipHold"/>, back to 1 by <see cref="DipSeconds"/>.</summary>
    public const float DipScale = 0.35f, DipHold = 0.1f, DipSeconds = 0.4f;

    private float _framing, _letterbox;
    private bool _dip;
    private float _skipAt = -1f;
    private bool _skipQueued;
    private float _skipAlpha, _skipBars, _skipFraming;

    public BossCardKind Kind { get; private set; }

    /// <summary>The boss's name (its boss bar title).</summary>
    public string Title { get; private set; } = "";

    /// <summary>The line under the name: the intro's subtitle, or "Defeated".</summary>
    public string Subtitle { get; private set; } = "";

    /// <summary>The subtitle and ornament colour (#RRGGBB).</summary>
    public string Color { get; private set; } = "";

    /// <summary>The boss whose card this is (its enemy id; the head icon).</summary>
    public string BossId { get; private set; } = "";

    /// <summary>Seconds since the card started.</summary>
    public float Time { get; private set; }

    /// <summary>Cards started so far (the <c>boss.intro_cards</c> probe).</summary>
    public int Shown { get; private set; }

    /// <summary>Skips that took effect so far.</summary>
    public int Skips { get; private set; }

    public bool Active => Kind != BossCardKind.None;

    public bool Skipped => _skipAt >= 0f;

    /// <summary>The card's length when nothing skips it.</summary>
    public float Duration => FadeIn + (Kind == BossCardKind.Defeated ? DefeatHold : Hold) + FadeOut;

    /// <summary>A boss awakens: its card, the camera pulled back <paramref name="framing"/> m and bars for <paramref name="letterbox"/> s.</summary>
    public void Awaken(string bossId, string title, string subtitle, string color, float framing, float letterbox)
    {
        Begin(BossCardKind.Awoken, bossId, title, subtitle, color);
        _framing = Math.Clamp(framing, 0f, MaxFramingDistance);
        _letterbox = Math.Clamp(letterbox, 0f, MaxLetterbox);
    }

    /// <summary>A boss falls: its name over "Defeated", shorter bars, and a slow-motion dip when <paramref name="slowMotion"/>.</summary>
    public void Defeat(string bossId, string title, string color, float letterbox, bool slowMotion)
    {
        Begin(BossCardKind.Defeated, bossId, title, "Defeated", color);
        _framing = 0f;
        _letterbox = Math.Clamp(letterbox, 0f, DefeatLetterbox);
        _dip = slowMotion;
    }

    private void Begin(BossCardKind kind, string bossId, string title, string subtitle, string color)
    {
        Kind = kind;
        BossId = bossId;
        Title = title;
        Subtitle = subtitle;
        Color = color;
        Time = 0f;
        _dip = false;
        _skipAt = -1f;
        _skipQueued = false;
        Shown++;
    }

    /// <summary>Hides the card at once (leaving the world); nothing eases out.</summary>
    public void Clear()
    {
        Kind = BossCardKind.None;
        Time = 0f;
        _dip = false;
        _skipAt = -1f;
        _skipQueued = false;
    }

    /// <summary>
    /// Fades the card, bars and framing out now (or once the card has shown <see cref="MinShown"/>). False when no card
    /// is up or it is already going.
    /// </summary>
    public bool Skip()
    {
        if (!Active || Skipped || _skipQueued) return false;
        if (Time < MinShown) _skipQueued = true;
        else BeginSkip();
        return true;
    }

    private void BeginSkip()
    {
        _skipAlpha = Alpha;
        _skipBars = Bars;
        _skipFraming = Framing;
        _skipAt = Time;
        _skipQueued = false;
        Skips++;
    }

    /// <summary>Moves the timeline on by <paramref name="seconds"/> (real time); the card ends when its time is up.</summary>
    public void Advance(float seconds)
    {
        if (!Active) return;
        Time += Math.Max(0f, seconds);
        if (_skipQueued && Time >= MinShown) BeginSkip();
        if (Skipped ? Time >= _skipAt + SkipFade : Time >= Duration) Clear();
    }

    /// <summary>The card's opacity (0–1).</summary>
    public float Alpha
    {
        get
        {
            if (!Active) return 0f;
            if (Skipped) return _skipAlpha * SkipLeft;
            float hold = Kind == BossCardKind.Defeated ? DefeatHold : Hold;
            if (Time < FadeIn) return Smooth(Time / FadeIn);
            return 1f - Smooth((Time - FadeIn - hold) / FadeOut);
        }
    }

    /// <summary>The title's scale: it settles from <see cref="PopScale"/> to 1 as the card fades in.</summary>
    public float TitleScale => !Active ? 1f : PopScale + (1f - PopScale) * Smooth(Time / FadeIn);

    /// <summary>How far the cinema bars are in (0–1).</summary>
    public float Bars
    {
        get
        {
            if (!Active || _letterbox <= 0f) return 0f;
            if (Skipped) return _skipBars * SkipLeft;
            return Math.Min(Smooth(Time / BarsIn), 1f - Smooth((Time - _letterbox) / BarsOut));
        }
    }

    /// <summary>Extra third-person camera distance now (m); zero again by <see cref="FramingSeconds"/>.</summary>
    public float Framing
    {
        get
        {
            if (!Active || _framing <= 0f) return 0f;
            if (Skipped) return _skipFraming * SkipLeft;
            return _framing * Math.Min(Smooth(Time / FramingIn), 1f - Smooth((Time - (FramingSeconds - FramingOut)) / FramingOut));
        }
    }

    /// <summary>The world's time scale the card asks for: 1, or the defeat's slow-motion dip.</summary>
    public float TimeScale
    {
        get
        {
            if (!Active || !_dip || Time >= DipSeconds) return 1f;
            return DipScale + (1f - DipScale) * Smooth((Time - DipHold) / (DipSeconds - DipHold));
        }
    }

    /// <summary>True while the slow-motion dip runs.</summary>
    public bool Dipping => Active && _dip && Time < DipSeconds;

    private float SkipLeft => 1f - Math.Clamp((Time - _skipAt) / SkipFade, 0f, 1f);

    /// <summary>Smoothstep of <paramref name="t"/> clamped to 0–1.</summary>
    private static float Smooth(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return t * t * (3f - 2f * t);
    }
}
