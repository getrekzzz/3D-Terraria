namespace Strata.Core.Content.Specs;

/// <summary>Parameters for enemy AI brains (implemented in Strata.Core.AI.Brains).</summary>
public static class BrainSpecs
{
    public const string Idle = "idle";
    public const string SlimeHop = "slimeHop";
    public const string Walker = "walker";
    public const string Flyer = "flyer";
    public const string GelSovereign = "gelSovereign";
    public const string Watcher = "watcher";
    public const string Worm = "worm";

    public static void Register(SpecCatalog c)
    {
        c.Register<IdleBrainParams>(SpecKind.Brain, Idle);
        c.Register<SlimeHopParams>(SpecKind.Brain, SlimeHop);
        c.Register<WalkerParams>(SpecKind.Brain, Walker);
        c.Register<FlyerParams>(SpecKind.Brain, Flyer);
        c.Register<GelSovereignParams>(SpecKind.Brain, GelSovereign);
        c.Register<WatcherParams>(SpecKind.Brain, Watcher);
        c.Register<WormParams>(SpecKind.Brain, Worm);
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

    /// <summary>Flees in daylight (undead-like) instead of burning.</summary>
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
}
