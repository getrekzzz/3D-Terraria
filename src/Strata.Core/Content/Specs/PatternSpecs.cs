namespace Strata.Core.Content.Specs;

/// <summary>
/// A boss (or elite) described as data: phases chosen by remaining life, each a loop of moves (hover, chase, dash,
/// orbit, teleport, hop, walk, wait) with optional projectile volleys, minion summons and a shield that holds while
/// guardians live. See <c>AI.PatternBrain</c>.
/// </summary>
public sealed class PatternParams : SpecParams, IValidatableSpec
{
    /// <summary>Walks and hops under gravity (Deerclops, Golem) instead of flying.</summary>
    public bool Ground { get; init; }

    /// <summary>Phases in order; a phase runs while life ≤ its <see cref="PatternPhase.Below"/> (the last match wins).</summary>
    public PatternPhase[] Phases { get; init; } = Array.Empty<PatternPhase>();

    public string? Validate()
    {
        if (Phases.Length == 0) return "needs at least one phase";
        foreach (var p in Phases)
        {
            if (p.Moves.Length == 0) return "every phase needs moves";
            foreach (var m in p.Moves)
                if (Array.IndexOf(PatternMove.Kinds, m.Type) < 0) return $"unknown move '{m.Type}' (expected {string.Join(", ", PatternMove.Kinds)})";
        }
        return null;
    }
}

public sealed class PatternPhase
{
    /// <summary>Life fraction (0–1) at or below which this phase applies.</summary>
    public float Below { get; init; } = 1f;

    public PatternMove[] Moves { get; init; } = Array.Empty<PatternMove>();

    /// <summary>Fired throughout the phase.</summary>
    public PatternVolley? Volley { get; init; }

    public PatternSummon? Summon { get; init; }

    /// <summary>Model shown during the phase (a revealed eye, an opened mouth).</summary>
    public string? Model { get; init; }

    /// <summary>Invulnerable while any creature of these kinds lives (the Brain's Creepers; comma-separated ids).</summary>
    public string? ShieldedBy { get; init; }

    /// <summary>Contact damage and movement speed multipliers.</summary>
    public float Damage { get; init; } = 1f;

    public float Speed { get; init; } = 1f;

    /// <summary>Announced when the phase begins (empty = nothing).</summary>
    public string? Message { get; init; }
}

public sealed class PatternMove
{
    public static readonly string[] Kinds = { "hover", "chase", "dash", "orbit", "teleport", "hop", "walk", "wait" };

    public string Type { get; init; } = "hover";

    /// <summary>How long the move lasts (ticks); for dashes and hops, how long each one lasts.</summary>
    public int Ticks { get; init; } = 120;

    /// <summary>Speed in m/s (orbit: along the circle).</summary>
    public float Speed { get; init; } = 8f;

    /// <summary>Metres above the target (hover, orbit, teleport) or jump height in cells (hop).</summary>
    public float Height { get; init; } = 5f;

    /// <summary>Horizontal distance from the target (hover, orbit, teleport).</summary>
    public float Distance { get; init; } = 8f;

    /// <summary>Repetitions (dashes, hops).</summary>
    public int Count { get; init; } = 1;

    /// <summary>Wind-up before each dash, hop or teleport (ticks).</summary>
    public int Telegraph { get; init; } = 30;

    /// <summary>Fired during this move (in addition to the phase volley).</summary>
    public PatternVolley? Volley { get; init; }

    /// <summary>Animation pose while moving.</summary>
    public string? Pose { get; init; }
}

public sealed class PatternVolley
{
    public string Projectile { get; init; } = "";
    public int Damage { get; init; } = 20;
    public float Speed { get; init; } = 14f;
    public int Interval { get; init; } = 90;
    public int Count { get; init; } = 1;

    /// <summary>Random spread in degrees; with <see cref="Ring"/>, projectiles fan evenly all around instead.</summary>
    public float Spread { get; init; }

    public bool Ring { get; init; }

    /// <summary>Fires only within this distance (m).</summary>
    public float Range { get; init; } = 45f;
}

public sealed class PatternSummon
{
    public string Enemy { get; init; } = "";
    public int Interval { get; init; } = 240;

    /// <summary>Most alive at once.</summary>
    public int Max { get; init; } = 4;

    public int Count { get; init; } = 1;

    /// <summary>Summon <see cref="Max"/> at once when the phase starts, then never again.</summary>
    public bool Once { get; init; }

    /// <summary>Kinds summoned at random instead of <see cref="Enemy"/> (a pillar's legion); Max counts them all.</summary>
    public string[] Pool { get; init; } = Array.Empty<string>();

    /// <summary>How far around the summoner they appear (m); beyond 3 m walkers are stood on the ground there.</summary>
    public float Radius { get; init; } = 2f;

    /// <summary>Summons only while a player is within this distance (m); 0 = always.</summary>
    public float Range { get; init; }
}
