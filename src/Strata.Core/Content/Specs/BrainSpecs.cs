namespace Strata.Core.Content.Specs;

/// <summary>Parameters for enemy AI brains (implemented in Strata.Core.AI.Brains).</summary>
public static class BrainSpecs
{
    public const string Idle = "idle";
    public const string SlimeHop = "slimeHop";
    public const string Walker = "walker";
    public const string Flyer = "flyer";
    public const string GelSovereign = "kingSlime";
    public const string Watcher = "eyeOfCthulhu";
    public const string Worm = "worm";
    public const string Swimmer = "swimmer";
    public const string Lunger = "lunger";
    public const string CryptWarden = "skeletron";
    public const string Hand = "skeletronHand";
    public const string Caster = "caster";
    public const string Mawwall = "wallOfFlesh";
    public const string Pattern = "pattern";

    public static void Register(SpecCatalog c)
    {
        c.Register<IdleBrainParams>(SpecKind.Brain, Idle);
        c.Register<SlimeHopParams>(SpecKind.Brain, SlimeHop);
        c.Register<WalkerParams>(SpecKind.Brain, Walker);
        c.Register<FlyerParams>(SpecKind.Brain, Flyer);
        c.Register<GelSovereignParams>(SpecKind.Brain, GelSovereign);
        c.Register<WatcherParams>(SpecKind.Brain, Watcher);
        c.Register<WormParams>(SpecKind.Brain, Worm);
        c.Register<SwimmerParams>(SpecKind.Brain, Swimmer);
        c.Register<LungerParams>(SpecKind.Brain, Lunger);
        c.Register<CryptWardenParams>(SpecKind.Brain, CryptWarden);
        c.Register<HandParams>(SpecKind.Brain, Hand);
        c.Register<CasterParams>(SpecKind.Brain, Caster);
        c.Register<MawwallParams>(SpecKind.Brain, Mawwall);
        c.Register<PatternParams>(SpecKind.Brain, Pattern);
    }
}

public sealed class IdleBrainParams : SpecParams
{
}

public sealed class SlimeHopParams : SpecParams
{
    /// <summary>Jump apex height in cells.</summary>
    public float JumpHeight { get; init; } = 5f;

    /// <summary>Horizontal jump speed in m/s.</summary>
    public float JumpSpeed { get; init; } = 3.5f;

    /// <summary>Ticks between hops.</summary>
    public int HopInterval { get; init; } = 70;

    /// <summary>Every Nth hop is a big hop.</summary>
    public int BigHopEvery { get; init; } = 3;

    public float AggroRange { get; init; } = 40f;
}

public sealed class WalkerParams : SpecParams
{
    public float AggroRange { get; init; } = 60f;

    /// <summary>Highest ledge (cells) it will jump up.</summary>
    public float JumpHeight { get; init; } = 4f;

    /// <summary>Wanders when no target.</summary>
    public bool Wander { get; init; } = true;

    /// <summary>Undead-like: gives up at dawn on the surface and walks away on one heading, despawning out of sight.</summary>
    public bool FleeDaylight { get; init; }
}

public sealed class FlyerParams : SpecParams
{
    public float AggroRange { get; init; } = 70f;

    /// <summary>Preferred orbit radius around the target in cells.</summary>
    public float OrbitRadius { get; init; } = 10f;

    /// <summary>Dash speed in m/s.</summary>
    public float DashSpeed { get; init; } = 12f;

    public int DashCooldown { get; init; } = 150;

    /// <summary>Bounces off terrain instead of stopping.</summary>
    public bool BounceOffTerrain { get; init; } = true;

    /// <summary>Gives up the chase at dawn on the surface and flies off (demon eyes).</summary>
    public bool FleeDaylight { get; init; }
}

/// <summary>Giant gel boss: big hops, slams, teleports next to a distant target, sheds minions when hurt.</summary>
public sealed class GelSovereignParams : SpecParams
{
    public float JumpHeight { get; init; } = 8f;
    public float JumpSpeed { get; init; } = 5f;
    public int HopInterval { get; init; } = 50;

    /// <summary>Teleports when the target is farther than this (m) or out of sight for a while.</summary>
    public float TeleportDistance { get; init; } = 28f;

    /// <summary>Minion enemy shed every <see cref="MinionEveryLife"/> fraction of life lost.</summary>
    public string? Minion { get; init; }
    public float MinionEveryLife { get; init; } = 0.08f;

    /// <summary>Teleports to a target it has not seen (from its head to their eyes) for this long (ticks).</summary>
    public int LostSightTicks { get; init; } = 300;

    /// <summary>Expert: every third shed minion is this one instead (Spiked Slimes).</summary>
    public string? ExpertMinion { get; init; }

    /// <summary>Teleport wind-up (ticks) in Classic and in Expert.</summary>
    public int TeleportTicks { get; init; } = 50;

    public int ExpertTeleportTicks { get; init; } = 35;

    /// <summary>Model scale (and collision box) at full life and at none: it shrinks as it loses gel.</summary>
    public float FullScale { get; init; } = 1f;

    public float EmptyScale { get; init; } = 0.6f;

    /// <summary>Landing faster than this (m/s) shakes the ground (an impact and the "land" cue).</summary>
    public float ImpactSpeed { get; init; } = 8f;
}

/// <summary>Floating eye boss: orbits and summons servants, then dashes in bursts; phase 2 below a life fraction.</summary>
public sealed class WatcherParams : SpecParams
{
    public float OrbitRadius { get; init; } = 14f;
    public float DashSpeed { get; init; } = 16f;
    public int DashesPerBurst { get; init; } = 3;
    public int TelegraphTicks { get; init; } = 30;
    public string? Servant { get; init; }
    public int ServantInterval { get; init; } = 90;
    public float Phase2Life { get; init; } = 0.5f;

    /// <summary>Model shown in phase 2 (the eye opens into a maw).</summary>
    public string? Phase2Model { get; init; }

    /// <summary>Defense in phase 2 (−1 = unchanged; the Eye's falls to 0) and the contact damage multiplier.</summary>
    public int Phase2Defense { get; init; } = -1;

    public float Phase2Damage { get; init; } = 1f;

    /// <summary>Extra dashes per burst in phase 2.</summary>
    public int Phase2ExtraDashes { get; init; } = 2;

    /// <summary>Entering phase 2 it spins in place, untouchable, for this long (0 = no transformation).</summary>
    public int TransformTicks { get; init; }

    /// <summary>Expert, below phase 2: one more dash per tenth of life lost, up to this many per burst (0 = off).</summary>
    public int ExpertMaxDashes { get; init; }

    /// <summary>Expert phase 2: wind-up before each dash scaled by this.</summary>
    public float ExpertTelegraphScale { get; init; } = 1f;
}

/// <summary>Burrowing worm: steers its head towards the target through terrain, carving a tunnel.</summary>
public sealed class WormParams : SpecParams
{
    public float Speed { get; init; } = 9f;
    public float TurnRate { get; init; } = 2.2f;

    /// <summary>Tunnel radius in cells (0 = does not dig).</summary>
    public float DigRadius { get; init; } = 1.6f;

    /// <summary>Ticks between carves.</summary>
    public int DigInterval { get; init; } = 8;

    /// <summary>Gravity applied while outside terrain (makes it arc back into the ground).</summary>
    public float AirGravity { get; init; } = 14f;

    /// <summary>Released from a random body segment every <see cref="MinionInterval"/> ticks (probes), up to <see cref="MaxMinions"/> alive.</summary>
    public string? Minion { get; init; }
    public int MinionInterval { get; init; } = 240;
    public int MaxMinions { get; init; } = 6;

    /// <summary>Spat from a random body segment that sees the target every <see cref="SpitInterval"/> ticks (Vile Spit).</summary>
    public string? SpitProjectile { get; init; }
    public int SpitInterval { get; init; } = 180;
    public int SpitDamage { get; init; } = 18;
    public float SpitSpeed { get; init; } = 8f;

    /// <summary>Spits only in Expert (the Eater of Worlds).</summary>
    public bool SpitExpertOnly { get; init; }
}

/// <summary>Lives in liquid: chases targets through water, flops helplessly on land.</summary>
public sealed class SwimmerParams : SpecParams
{
    public float AggroRange { get; init; } = 30f;

    /// <summary>Moves in bursts (jellyfish) instead of steadily (fish).</summary>
    public bool Pulse { get; init; }

    /// <summary>Only chases players who are in liquid themselves.</summary>
    public bool ChaseOnlyInLiquid { get; init; } = true;
}

/// <summary>Rooted where it spawned; its head lunges at targets within reach (snatcher vines).</summary>
public sealed class LungerParams : SpecParams, IValidatableSpec
{
    /// <summary>Longest stretch of the stalk (cells).</summary>
    public float Reach { get; init; } = 8f;

    public float AggroRange { get; init; } = 16f;

    public string? Validate() => Reach <= 0 ? "reach must be > 0" : null;
}

/// <summary>
/// A floating skull with two hands (boss parts, or legacy segments for Prime): hovers over the target, then spins
/// through walls at it with lowered defense; in Expert its living hands armour it and it fires homing skulls.
/// </summary>
public sealed class CryptWardenParams : SpecParams
{
    /// <summary>Hover height above the target (m).</summary>
    public float HoverHeight { get; init; } = 6f;

    /// <summary>Segment hands (Prime): distance of each hand from the skull (m).</summary>
    public float HandSpacing { get; init; } = 2.6f;

    /// <summary>Segment hands (Prime): ticks between hand swipes (hands alternate).</summary>
    public int SwipeInterval { get; init; } = 100;

    /// <summary>Ticks of hovering before a spin charge, and the charge's length (Terraria: 800 and 400).</summary>
    public int SpinInterval { get; init; } = 800;
    public int SpinDuration { get; init; } = 400;

    public float SpinSpeed { get; init; } = 9f;

    /// <summary>Defense added while spinning (Terraria: −10).</summary>
    public int SpinDefense { get; init; }

    /// <summary>Movement speed added per lost hand (0.25 = a quarter faster for each).</summary>
    public float SpeedPerLostHand { get; init; } = 0.25f;

    /// <summary>Expert: defense each living hand adds while hovering.</summary>
    public int ExpertDefensePerHand { get; init; }

    /// <summary>
    /// Expert: hostile projectile fired at the target every <see cref="SkullInterval"/> ticks (half that with no hands)
    /// while hovering, once a hand has fallen or life is below <see cref="SkullLife"/>.
    /// </summary>
    public string? Skull { get; init; }
    public int SkullDamage { get; init; } = 17;
    public float SkullSpeed { get; init; } = 8f;
    public int SkullInterval { get; init; } = 80;
    public float SkullLife { get; init; } = 0.75f;

    /// <summary>Segment hands (Prime): only the first N hands swipe (the rest are guns that hold position); 0 = every hand.</summary>
    public int SwipingHands { get; init; }

    /// <summary>Below this life fraction the warden is enraged (faster swipes, enraged ranged attack); 0 = never.</summary>
    public float Phase2Life { get; init; }
}

/// <summary>Stands and casts (see the enemy's ranged attack), blinking to a new spot near the target now and then.</summary>
public sealed class CasterParams : SpecParams, IValidatableSpec
{
    /// <summary>Cells (like the other brains).</summary>
    public float AggroRange { get; init; } = 50f;
    public int TeleportInterval { get; init; } = 300;

    /// <summary>Blink destination distance from the target (m).</summary>
    public float MinBlink { get; init; } = 5f;
    public float MaxBlink { get; init; } = 11f;

    public string? Validate() => MaxBlink < MinBlink ? "maxBlink must be >= minBlink" : null;
}

/// <summary>
/// A wall of flesh filling the underworld cavity from lava to ceiling that sweeps along X towards the far side of
/// the world, keeping its width centred on its target; eyes (segments) fire, the mouth (body) bites and spits tethered
/// minions, and it speeds up as it weakens or falls behind.
/// </summary>
public sealed class MawwallParams : SpecParams, IValidatableSpec
{
    /// <summary>How far behind the target the wall appears (m), on the side of the nearer world edge.</summary>
    public float StartDistance { get; init; } = 45f;

    /// <summary>Wall width across Z (m) and thickness along X (m).</summary>
    public float Width { get; init; } = 40f;
    public float Thickness { get; init; } = 3f;

    /// <summary>Sweep speed (m/s) at full life and at no life.</summary>
    public float BaseSpeed { get; init; } = 1.6f;
    public float MaxSpeed { get; init; } = 5f;

    /// <summary>Beyond this lead (m) the target has over the wall it hurries (double speed).</summary>
    public float CatchUpDistance { get; init; } = 60f;

    /// <summary>How fast the wall slides sideways to stay centred on its target (m/s).</summary>
    public float TrackSpeed { get; init; } = 3f;

    /// <summary>Vertical distance between the two eyes (m).</summary>
    public float EyeSpacing { get; init; } = 7f;

    /// <summary>Tethered minion spat from the wall's face, how often (ticks) and how many at once.</summary>
    public string? Minion { get; init; }
    public int MinionInterval { get; init; } = 300;
    public int MaxMinions { get; init; } = 5;

    public string? Validate() =>
        Width <= 0 || Thickness <= 0 ? "width and thickness must be > 0"
        : MaxSpeed < BaseSpeed ? "maxSpeed must be >= baseSpeed"
        : MinionInterval < 1 ? "minionInterval must be >= 1" : null;
}
