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

    /// <summary>Colour of the ribbon trail it leaves (null = none).</summary>
    public string? Trail { get; init; }

    /// <summary>Trail width at the head, metres (it tapers to nothing at the tail).</summary>
    public float TrailWidth { get; init; } = 0.12f;

    /// <summary>How long (s) a point of the trail lasts; the trail lingers this long after the shot ends.</summary>
    public float TrailTime { get; init; } = 0.25f;

    /// <summary>How fast the model spins in flight (rad/s); 0 = boomerangs whirl and fused throws tumble, others fly straight.</summary>
    public float Spin { get; init; }

    /// <summary>The spin's axis: <c>tumble</c> (end over end), <c>flat</c> (a disc: shuriken, boomerangs) or <c>roll</c> (about the flight).</summary>
    public string SpinAxis { get; init; } = "tumble";

    public static readonly string[] SpinAxes = { "tumble", "flat", "roll" };

    /// <summary>Held on a string or chain from the hand (yoyos, flails; P3-12/13), or null.</summary>
    public TetherDef? Tether { get; init; }

    /// <summary>Only one may be out per player by default: returning throws and tethered ones.</summary>
    public bool OneAtATime => Returns > 0 || Tether != null;

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

/// <summary>
/// A projectile held on a string or chain (P3-12 yoyos, P3-13 flails): it stays out while the use button is held, up
/// to <see cref="Lifetime"/> ticks (0 = for as long as it is held), within <see cref="Range"/> cells of the eye, then
/// comes back to the hand.
/// </summary>
public sealed class TetherDef
{
    public static readonly string[] Kinds = { "yoyo", "flail" };

    public string Kind { get; init; } = "yoyo";

    /// <summary>How far (cells) from the eye it may go.</summary>
    public float Range { get; init; } = 14f;

    /// <summary>How long (ticks) it may stay out while held; 0 = without limit.</summary>
    public int Lifetime { get; init; }

    /// <summary>Top speed (m/s) chasing the crosshair.</summary>
    public float Speed { get; init; } = 9f;

    /// <summary>The string's (or chain's) colour.</summary>
    public string String { get; init; } = "#e8e8e8";
}
