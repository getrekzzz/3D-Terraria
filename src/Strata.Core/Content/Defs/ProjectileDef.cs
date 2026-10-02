namespace Strata.Core.Content.Defs;

/// <summary>A projectile's physical and visual behaviour.</summary>
public sealed class ProjectileDef : ContentDef
{
    public string? Model { get; init; }

    /// <summary>Visual style used when no model is given (arrow, bolt, orb, spark, bullet).</summary>
    public string Visual { get; init; } = "orb";

    public string Color { get; init; } = "#ffffff";

    /// <summary>Gravity in m/s² (0 for magic bolts).</summary>
    public float Gravity { get; init; }

    public float Drag { get; init; }

    public int Lifetime { get; init; } = 180;

    /// <summary>Collision radius in cells.</summary>
    public float Radius { get; init; } = 0.4f;

    /// <summary>Number of enemies it can pass through (-1 = infinite).</summary>
    public int Pierce { get; init; } = 1;

    public int Bounces { get; init; }
    public bool CollideTerrain { get; init; } = true;
    public bool Friendly { get; init; } = true;
    public bool Hostile { get; init; }

    public int[]? Light { get; init; }
    public string? Trail { get; init; }

    public TypedSpec[] OnHit { get; init; } = Array.Empty<TypedSpec>();

    /// <summary>Runs its onHit effects when its lifetime runs out: bombs, dynamite and grenades explode on a fuse.</summary>
    public bool Fuse { get; init; }

    /// <summary>Damages creatures it touches; bombs only hurt by exploding (false).</summary>
    public bool Contact { get; init; } = true;

    /// <summary>
    /// Boomerangs and flails: after this many ticks, or on striking anything, fly back to the thrower and vanish
    /// in their hand (0 = never returns). Only one is out at a time.
    /// </summary>
    public int Returns { get; init; }

    /// <summary>Steers toward the nearest creature (turn rate in radians per second; 0 = flies straight).</summary>
    public float Homing { get; init; }

    /// <summary>
    /// Steers along the owner's aim while they hold the item that fired it (Magic Missile follows the crosshair):
    /// turn rate like <see cref="Homing"/>; 0 = not guided.
    /// </summary>
    public float Guided { get; init; }

    /// <summary>Fishing bobbers float on liquid and wait for a bite.</summary>
    public bool Bobber { get; init; }

    /// <summary>Model scale for "item" and "creature" visuals.</summary>
    public float Scale { get; init; } = 1f;

    /// <summary>
    /// Sticks to the first creature it hits (Piranha Gun): rides along at the spot it bit and strikes again every
    /// <see cref="LatchInterval"/> ticks until its lifetime ends or the creature dies.
    /// </summary>
    public bool Latch { get; init; }

    public int LatchInterval { get; init; } = 10;

    /// <summary>Summoned minion behaviour (summon staves); null for ordinary projectiles.</summary>
    public MinionDef? Minion { get; init; }

    /// <summary>A pet or light pet summoned by the item in a misc slot (never expires, harmless); null for other projectiles.</summary>
    public PetDef? Pet { get; init; }
}

/// <summary>
/// A pet's movement (Core ProjectileSystem.Pets.cs): fliers hover behind the owner's shoulder with a gentle bob; walkers
/// follow on the ground, hopping one-cell steps. Both catch up at once when left more than 30 m behind.
/// </summary>
public sealed class PetDef
{
    public bool Flies { get; init; }

    /// <summary>Top speed (m/s) when the owner is a little way off; it runs faster the further behind it falls.</summary>
    public float Speed { get; init; } = 6f;

    /// <summary>How far behind the owner it settles (m).</summary>
    public float Follow { get; init; } = 1.6f;

    /// <summary>Fliers: height of its hover above the owner's centre (m).</summary>
    public float Hover { get; init; } = 0.9f;
}

/// <summary>A summoned minion: follows its owner and attacks creatures near them until the owner dies.</summary>
public sealed class MinionDef
{
    /// <summary>Flight speed (m/s; 0 = a sentry that stays where it was summoned) and how far from the owner it looks for targets (m).</summary>
    public float Speed { get; init; } = 9f;

    public float Range { get; init; } = 22f;

    /// <summary>Ranged minions (imps) fire this projectile instead of ramming.</summary>
    public string? Shoot { get; init; }

    public int ShootInterval { get; init; } = 60;
    public float ShootSpeed { get; init; } = 14f;

    /// <summary>Minion slots used (the owner has 1 plus the maxMinions stat); sentries have their own single slot.</summary>
    public int Slots { get; init; } = 1;

    /// <summary>A sentry (speed 0) stays where it was summoned and does not take a minion slot.</summary>
    public bool IsSentry => Speed <= 0;
}
