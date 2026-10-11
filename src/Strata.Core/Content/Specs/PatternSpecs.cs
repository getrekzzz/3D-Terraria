namespace Strata.Core.Content.Specs;

/// <summary>
/// A boss (or elite) described as data: phases chosen by remaining life, each a loop of moves (hover, chase, dash,
/// orbit, teleport, hop, walk, wait, slam) with optional projectile volleys, minion summons, a shield that holds while
/// guardians live, Expert overrides, flight, a leash to anchors, debuff auras and illusions. See <c>AI.PatternBrain</c>.
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
            {
                if (Array.IndexOf(PatternMove.Kinds, m.Type) < 0) return $"unknown move '{m.Type}' (expected {string.Join(", ", PatternMove.Kinds)})";
                if (m.Type == "slam" && !Ground && !p.Fly) return "'slam' needs a ground boss or a fly phase";
                if (m.Track < 0) return "track must be >= 0";
            }
            if (Array.TrueForAll(p.Moves, m => m.ExpertOnly)) return "every phase needs a move that is not expertOnly";
            if (p.ExpertBelow is { } eb && (eb <= 0f || eb > 1f)) return "expertBelow must be in (0, 1]";
            if (p.Summon is { Tether: true } t && !t.Once && t.Max <= 0) return "a tethered summon needs once or max > 0";
            if (p.Aura is { } a && (a.Interval <= 0 || a.Radius <= 0)) return "aura needs interval and radius > 0";
            if (p.Leash is { } l && l.Radius <= 0) return "leash radius must be > 0";
            if (p.Illusions < 0) return "illusions must be >= 0";
            if (p.UntilGuardsFall && string.IsNullOrEmpty(p.ShieldedBy)) return "untilGuardsFall needs shieldedBy";
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

    /// <summary>The phase ends (the next one begins, whatever the life) once its <see cref="ShieldedBy"/> guardians fall.</summary>
    public bool UntilGuardsFall { get; init; }

    /// <summary>Contact damage and movement speed multipliers.</summary>
    public float Damage { get; init; } = 1f;

    public float Speed { get; init; } = 1f;

    /// <summary>Announced when the phase begins (empty = nothing).</summary>
    public string? Message { get; init; }

    /// <summary>Expert and Master overrides (speed, damage, volleys, summons, wind-ups).</summary>
    public PatternExpert? Expert { get; init; }

    /// <summary>The life threshold used instead of <see cref="Below"/> in Expert (an earlier second phase).</summary>
    public float? ExpertBelow { get; init; }

    /// <summary>A ground boss takes to the air: no gravity while the phase runs.</summary>
    public bool Fly { get; init; }

    /// <summary>A debuff pulsed on nearby players.</summary>
    public PatternAura? Aura { get; init; }

    /// <summary>Keeps the boss near its tethered anchors.</summary>
    public PatternLeash? Leash { get; init; }

    /// <summary>Decoy copies shown mirrored around the target (presentation only).</summary>
    public int Illusions { get; init; }

    /// <summary>Defense while the phase runs (replaces the definition's).</summary>
    public int? Defense { get; init; }
}

public sealed class PatternMove
{
    public static readonly string[] Kinds = { "hover", "chase", "dash", "orbit", "teleport", "hop", "walk", "wait", "slam" };

    public string Type { get; init; } = "hover";

    /// <summary>How long the move lasts (ticks); for dashes and hops, how long each one lasts; for a slam, the longest fall.</summary>
    public int Ticks { get; init; } = 120;

    /// <summary>Speed in m/s (orbit: along the circle).</summary>
    public float Speed { get; init; } = 8f;

    /// <summary>Metres above the target (hover, orbit, teleport, slam) or jump height in cells (hop).</summary>
    public float Height { get; init; } = 5f;

    /// <summary>Horizontal distance from the target (hover, orbit, teleport).</summary>
    public float Distance { get; init; } = 8f;

    /// <summary>Repetitions (dashes, hops).</summary>
    public int Count { get; init; } = 1;

    /// <summary>Wind-up before each dash, hop, teleport or slam (ticks).</summary>
    public int Telegraph { get; init; } = 30;

    /// <summary>
    /// Slam: how fast the boss follows the target through the wind-up and the fall (m/s, scaled like Speed); unset =
    /// max(Speed, 8), so it lands on the target. 0 stomps where it stands (Deerclops).
    /// </summary>
    public float? Track { get; init; }

    /// <summary>Only in Expert and Master worlds (skipped in Classic and Journey).</summary>
    public bool ExpertOnly { get; init; }

    /// <summary>Fired during this move (in addition to the phase volley); a slam fires it once on landing.</summary>
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

    /// <summary>Projectiles per volley in Expert (instead of Count + the phase's Expert.VolleyCount).</summary>
    public int? ExpertCount { get; init; }

    /// <summary>Interval in Expert (instead of Interval × the phase's Expert.VolleyInterval).</summary>
    public int? ExpertInterval { get; init; }
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

    /// <summary>Minions become followers of the boss (they die with it), spread on a ring of <see cref="Radius"/>.</summary>
    public bool Tether { get; init; }

    /// <summary>Added to where minions appear (m, [x, y, z]; a tethered minion keeps it from the boss).</summary>
    public float[] Offset { get; init; } = Array.Empty<float>();
}
