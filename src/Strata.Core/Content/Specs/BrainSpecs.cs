namespace Strata.Core.Content.Specs;

/// <summary>Parameters for enemy AI brains (implemented in Strata.Core.AI.Brains).</summary>
public static class BrainSpecs
{
    public const string Idle = "idle";
    public const string SlimeHop = "slimeHop";
    public const string Walker = "walker";
    public const string Flyer = "flyer";

    public static void Register(SpecCatalog c)
    {
        c.Register<IdleBrainParams>(SpecKind.Brain, Idle);
        c.Register<SlimeHopParams>(SpecKind.Brain, SlimeHop);
        c.Register<WalkerParams>(SpecKind.Brain, Walker);
        c.Register<FlyerParams>(SpecKind.Brain, Flyer);
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
