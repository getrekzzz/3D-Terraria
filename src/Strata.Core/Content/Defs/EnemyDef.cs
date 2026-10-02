namespace Strata.Core.Content.Defs;

/// <summary>A projectile a creature fires at its target (any brain).</summary>
public sealed class RangedAttack
{
    /// <summary>Hostile projectile (projectile with <c>friendly: false</c>).</summary>
    public string Projectile { get; init; } = "";

    public int Damage { get; init; } = 10;

    /// <summary>Launch speed (m/s).</summary>
    public float Speed { get; init; } = 12f;

    /// <summary>Ticks between volleys.</summary>
    public int Interval { get; init; } = 120;

    /// <summary>Ticks between volleys at no life: the interval eases from <see cref="Interval"/> to this as the creature weakens.</summary>
    public int? IntervalAtZeroLife { get; init; }

    /// <summary>Fires only within this distance (m) and with a clear line of sight.</summary>
    public float Range { get; init; } = 18f;

    public int Count { get; init; } = 1;

    /// <summary>Random spread per projectile (degrees).</summary>
    public float Spread { get; init; }

    /// <summary>Volleys come from the body parts (segments) in turn, e.g. a wall's eyes, instead of the body.</summary>
    public bool FromParts { get; init; }

    /// <summary>With <see cref="FromParts"/>: only these part indices fire (a cannon arm, not a saw); empty = all.</summary>
    public int[] Parts { get; init; } = Array.Empty<int>();

    /// <summary>Replaces this attack while the creature is enraged (a boss's second phase): e.g. rapid lasers.</summary>
    public RangedAttack? Enraged { get; init; }
}

/// <summary>A hostile or passive creature. Behaviour comes from its <see cref="Brain"/> spec.</summary>
public sealed class EnemyDef : ContentDef
{
    /// <summary>AI brain spec, e.g. { "type": "slimeHop", "jumpHeight": 5 }.</summary>
    public TypedSpec Brain { get; init; } = TypedSpec.Of("idle");

    public int Life { get; init; } = 10;
    public int Damage { get; init; }
    public int Defense { get; init; }

    /// <summary>0 = full knockback, 1 = immune.</summary>
    public float KnockbackResist { get; init; }

    /// <summary>Collision size [width, height] in cells.</summary>
    public float[] Size { get; init; } = { 2f, 2f };

    public float MoveSpeed { get; init; } = 3f;

    public bool Gravity { get; init; } = true;
    public bool CollideTerrain { get; init; } = true;
    public bool ContactDamage { get; init; } = true;

    /// <summary>Creature model name (assets/models/creatures/&lt;model&gt;.glb); several enemies may share one.</summary>
    public string? Model { get; init; }

    /// <summary>Colour multiplier applied to the model (recolours shared models such as gels; white = as modelled).</summary>
    public string Tint { get; init; } = "#ffffff";
    public float Scale { get; init; } = 1f;

    /// <summary>
    /// How the model turns (presentation only): "yaw" about the vertical (default), "look" fully toward its target (eyes
    /// that stare you down), "velocity" along its flight (fish, darting fliers).
    /// </summary>
    public string Facing { get; init; } = "yaw";

    /// <summary>Squashes on landing and stretches while rising (gels; slime-hoppers always do).</summary>
    public bool Squash { get; init; }

    public string? Loot { get; init; }

    /// <summary>Item a bug net turns this creature into (critters).</summary>
    public string? CatchItem { get; init; }

    /// <summary>Set for bosses (boss bar, arena, defeat flag, segments).</summary>
    public BossInfo? Boss { get; init; }

    /// <summary>Spawns posing as something harmless (a chest, a lost girl) until woken.</summary>
    public DisguiseInfo? Disguise { get; init; }

    /// <summary>Coins dropped (copper).</summary>
    public int Value { get; init; }

    public string? HitSound { get; init; }
    public string? DeathSound { get; init; }

    /// <summary>Emitted light [r,g,b] 0–15 (glowing creatures).</summary>
    public int[]? Light { get; init; }

    public RangedAttack? Ranged { get; init; }

    /// <summary>Cannot be harmed (guardians that enforce progression).</summary>
    public bool Invulnerable { get; init; }

    /// <summary>Colour of the stalk or tether drawn from its anchor (rooted and tethered creatures; default vine green).</summary>
    public string? TetherColor { get; init; }

    /// <summary>Leaves at once when these hold where its target stands (the crypt guardian once you step outside).</summary>
    public TypedSpec[] DespawnWhen { get; init; } = Array.Empty<TypedSpec>();

    /// <summary>Effects on a player this creature touches for damage (e.g. applyBuff on_fire).</summary>
    public TypedSpec[] OnContact { get; init; } = Array.Empty<TypedSpec>();

    /// <summary>Cannot be harmed until enough of its kind's creatures have fallen.</summary>
    public KillShield? Shield { get; init; }

    /// <summary>Effects when it dies (pourLiquid, spawnEnemies).</summary>
    public TypedSpec[] OnDeath { get; init; } = Array.Empty<TypedSpec>();

    public float Width => Size.Length > 0 ? Size[0] : 2f;
    public float Height => Size.Length > 1 ? Size[1] : 2f;

    /// <summary>
    /// Every creature model this enemy shows (head, body segments, tail, free parts, arms, barrier), distinct, for
    /// preloading.
    /// </summary>
    public IReadOnlyList<string> ModelNames()
    {
        var names = new List<string> { Model ?? Id };
        void Add(string? name)
        {
            if (name != null && !names.Contains(name)) names.Add(name);
        }
        if (Boss is { } boss)
        {
            Add(boss.SegmentModel);
            Add(boss.TailModel);
            foreach (var part in boss.SegmentModels) Add(part);
            Add(boss.ArmModel);
            Add(boss.BarrierModel);
        }
        return names;
    }
}

public enum SpawnPlacement
{
    /// <summary>Standing on ground with headroom (walkers, hoppers, low fliers).</summary>
    Ground,

    /// <summary>Inside a body of liquid at least two cells deep (swimmers).</summary>
    Liquid,
}

/// <summary>Where and when an enemy may naturally spawn.</summary>
public sealed class SpawnRuleDef : ContentDef
{
    public SpawnPlacement Placement { get; init; } = SpawnPlacement.Ground;

    /// <summary>Evaluate the conditions where the player stands rather than at the spawn cell (zone guardians).</summary>
    public bool AtPlayer { get; init; }

    /// <summary>No more spawns while this many of the enemy are alive (0 = no cap).</summary>
    public int MaxAlive { get; init; }

    /// <summary>Metres above the spawn cell (default: 2.5 for ground-spawned fliers, else 0; rooted creatures use 0).</summary>
    public float? Lift { get; init; }

    public string Enemy { get; init; } = "";
    public float Weight { get; init; } = 1f;
    public int[] Group { get; init; } = { 1, 1 };
    public TypedSpec[] Conditions { get; init; } = Array.Empty<TypedSpec>();

    public int GroupMin => Group.Length > 0 ? Group[0] : 1;
    public int GroupMax => Group.Length > 1 ? Group[1] : GroupMin;
}
