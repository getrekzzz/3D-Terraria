namespace Strata.Core.Content.Defs;

/// <summary>When in the day a hint may show.</summary>
public enum HintTime
{
    Any,
    Day,
    Night,

    /// <summary>The last in-game hours before nightfall (<see cref="HintDef.DuskHours"/>).</summary>
    Dusk,
}

/// <summary>Something that must be near the player for a hint (any one set field must match).</summary>
public sealed class HintNear
{
    /// <summary>A tile object id (e.g. <c>work_bench</c>).</summary>
    public string? Object { get; init; }

    /// <summary>Any tile object with this tag.</summary>
    public string? Tag { get; init; }

    /// <summary>Any tree (a trunk felled with an axe).</summary>
    public bool Tree { get; init; }

    /// <summary>A living town resident id (e.g. <c>guide</c>).</summary>
    public string? Resident { get; init; }

    /// <summary>Distance in metres (objects: half the cube edge, rounded to cells).</summary>
    public float Radius { get; init; } = 6f;
}

/// <summary>
/// An onboarding hint (data/hints.json): shown once per character in the hint toast while every condition holds and
/// no <see cref="Unless"/> trigger does. <see cref="When"/>/<see cref="Unless"/> are achievement triggers
/// (<see cref="Specs.AchievementSpecs"/>); <see cref="Text"/> may name input actions as <c>{key:action}</c>;
/// <see cref="Guide"/> names the Guide book entry (a guide step or tip id) the hint points to.
/// </summary>
public sealed class HintDef : ContentDef
{
    public const float DuskHours = 2.5f;

    public string Text { get; init; } = "";

    /// <summary>A guide step or guide tip id the toast links to ("See the Guide book: …").</summary>
    public string? Guide { get; init; }

    /// <summary>Higher shows first when several are due.</summary>
    public int Priority { get; init; }

    /// <summary>Seconds the player must have been in the world first.</summary>
    public float Delay { get; init; }

    /// <summary>Seconds the toast stays up.</summary>
    public float Duration { get; init; } = 7f;

    public TypedSpec[] When { get; init; } = Array.Empty<TypedSpec>();
    public TypedSpec[] Unless { get; init; } = Array.Empty<TypedSpec>();
    public HintTime Time { get; init; }
    public HintNear? Near { get; init; }

    /// <summary>An item the player must carry.</summary>
    public string? Carrying { get; init; }

    /// <summary>A guide step that must be the Guide's next advice (progression nudges).</summary>
    public string? Step { get; init; }
}
