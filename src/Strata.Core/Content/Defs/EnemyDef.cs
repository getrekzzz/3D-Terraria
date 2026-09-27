namespace Strata.Core.Content.Defs;

/// <summary>Multipliers a boss gets while the sun is up.</summary>
public sealed class DayEnrage
{
    public float Damage { get; init; } = 4f;
    public float Speed { get; init; } = 2f;
}

/// <summary>Boss metadata: presentation, arena, progression and body segments (worms).</summary>
public sealed class BossInfo
{
    /// <summary>
    /// A creature that only borrows the boss body (worm segments, free parts) — Wyverns, Devourers, Bone Serpents: no
    /// boss bar, announcements, music or flag, and it despawns like any creature.
    /// </summary>
    public bool Minor { get; init; }

    /// <summary>Shown on the boss bar and in announcements.</summary>
    public string Title { get; init; } = "";

    /// <summary>Progression flag set when defeated.</summary>
    public string? Flag { get; init; }

    /// <summary>More flags raised with <see cref="Flag"/> (either evil boss sets "defeated_evil_boss").</summary>
    public string[] AlsoFlags { get; init; } = Array.Empty<string>();

    /// <summary>Beyond this distance (m) from every player the boss leaves (despawns).</summary>
    public float ArenaRadius { get; init; } = 120f;

    /// <summary>Leaves when day breaks (night bosses).</summary>
    public bool DespawnAtDay { get; init; }

    /// <summary>Music state requested while alive.</summary>
    public string? Music { get; init; }

    /// <summary>Body segments following the head (worms); 0 = single body.</summary>
    public int Segments { get; init; }

    /// <summary>Distance between segment centres (m).</summary>
    public float SegmentSpacing { get; init; } = 1f;

    /// <summary>Collision radius of body segments (m).</summary>
    public float SegmentRadius { get; init; } = 0.6f;

    /// <summary>Creature model for body segments and for the last segment (defaults to the segment model).</summary>
    public string? SegmentModel { get; init; }

    public string? TailModel { get; init; }

    /// <summary>Model per segment index (free parts such as hands); overrides SegmentModel/TailModel.</summary>
    public string[] SegmentModels { get; init; } = Array.Empty<string>();

    /// <summary>Stronger by day instead of leaving (the crypt's curse): multipliers on damage and speed.</summary>
    public DayEnrage? DayEnrage { get; init; }

    /// <summary>Model tiled over the creature's <see cref="Gameplay.Entities.Npc.Barrier"/> (a wall's flesh), and its tile size (m).</summary>
    public string? BarrierModel { get; init; }

    public float BarrierTile { get; init; } = 4f;

    /// <summary>
    /// Bosses summoned alongside this one (a pair of eyes). They share its <see cref="Flag"/>, which is raised only when
    /// the last of the group falls; announcements and the boss bar treat the group as one.
    /// </summary>
    public string[] Companions { get; init; } = Array.Empty<string>();
}

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

    public string? Loot { get; init; }

    /// <summary>Set for bosses (boss bar, arena, defeat flag, segments).</summary>
    public BossInfo? Boss { get; init; }

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

    /// <summary>Effects when it dies (pourLiquid, spawnEnemies).</summary>
    public TypedSpec[] OnDeath { get; init; } = Array.Empty<TypedSpec>();

    public float Width => Size.Length > 0 ? Size[0] : 2f;
    public float Height => Size.Length > 1 ? Size[1] : 2f;
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
