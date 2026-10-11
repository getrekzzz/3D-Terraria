namespace Strata.Core.Content.Specs;

/// <summary>How a pattern phase plays in Expert and Master worlds (multipliers on the Classic values).</summary>
public sealed class PatternExpert
{
    /// <summary>Multiplier on every move speed.</summary>
    public float Speed { get; init; } = 1f;

    /// <summary>Multiplier on contact damage.</summary>
    public float Damage { get; init; } = 1f;

    /// <summary>Multiplier on volley intervals (below 1 fires more often).</summary>
    public float VolleyInterval { get; init; } = 1f;

    /// <summary>Projectiles added to every volley.</summary>
    public int VolleyCount { get; init; }

    /// <summary>Minions added to the summon's cap (and to a one-off summon's number).</summary>
    public int SummonMax { get; init; }

    /// <summary>Multiplier on wind-ups (dash, hop, teleport, slam); below 1 gives less warning.</summary>
    public float TelegraphScale { get; init; } = 1f;
}

/// <summary>A debuff the boss pulses on every player near it (the Brain's confusion).</summary>
public sealed class PatternAura
{
    public string Buff { get; init; } = "";

    /// <summary>How long the buff lasts (ticks).</summary>
    public int Ticks { get; init; } = 120;

    /// <summary>Ticks between pulses.</summary>
    public int Interval { get; init; } = 300;

    /// <summary>Reach (m).</summary>
    public float Radius { get; init; } = 20f;

    /// <summary>Pulses only in Expert and Master worlds.</summary>
    public bool ExpertOnly { get; init; }
}

/// <summary>Holds the boss within a radius of its living anchors (Plantera's hooks).</summary>
public sealed class PatternLeash
{
    /// <summary>The follower enemy id the boss hangs from (its tethered summons).</summary>
    public string Kind { get; init; } = "";

    /// <summary>Furthest the boss strays from the anchors' centroid (m).</summary>
    public float Radius { get; init; } = 10f;
}
