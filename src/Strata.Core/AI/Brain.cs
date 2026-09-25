using System.Numerics;
using Strata.Core.Content.Specs;
using Strata.Core.Determinism;
using Strata.Core.Gameplay.Entities;
using Strata.Core.Session;
using Strata.Core.Voxels;

namespace Strata.Core.AI;

/// <summary>What a brain may look at when deciding (the world, the nearest player, time, randomness).</summary>
public sealed class BrainContext
{
    public required VoxelWorld World { get; init; }
    public PlayerEntity? Target { get; set; }
    public float TargetDistance { get; set; }
    public bool IsDay { get; set; }
    public bool OnSurface { get; set; }
    public Pcg32 Rng;

    /// <summary>Spawns a minion (bosses); returns null when the creature cap is reached.</summary>
    public Func<Content.Defs.EnemyDef, Vector3, Npc?>? Spawn { get; set; }

    /// <summary>Removes natural terrain in a sphere (radius in cells) — burrowing creatures.</summary>
    public Action<Vector3, float>? Carve { get; set; }

    public required Content.ContentDatabase Content { get; init; }

    public const float Gravity = 22f;
}

/// <summary>Per-tick decision making for a creature: writes its velocity, facing and pose.</summary>
public interface IBrain
{
    /// <summary>Whether gravity applies to this creature.</summary>
    bool UsesGravity { get; }

    /// <summary>Moves through terrain without collision (burrowers); the brain handles everything.</summary>
    bool MovesFreely => false;

    void Think(Npc npc, BrainContext ctx);

    /// <summary>Called after movement with the velocity before collision (for bouncing).</summary>
    void AfterMove(Npc npc, Vector3 intended) { }
}

/// <summary>Creates brains from enemy brain specs (parsed once per definition).</summary>
public static class BrainFactory
{
    public static IBrain Create(Content.TypedSpec spec) => spec.Type switch
    {
        BrainSpecs.SlimeHop => new SlimeHopBrain(spec.Parse<SlimeHopParams>()),
        BrainSpecs.Walker => new WalkerBrain(spec.Parse<WalkerParams>()),
        BrainSpecs.Flyer => new FlyerBrain(spec.Parse<FlyerParams>()),
        BrainSpecs.Idle => new IdleBrain(),
        BrainSpecs.GelSovereign => new GelSovereignBrain(spec.Parse<GelSovereignParams>()),
        BrainSpecs.Watcher => new WatcherBrain(spec.Parse<WatcherParams>()),
        BrainSpecs.Worm => new WormBrain(spec.Parse<WormParams>()),
        BrainSpecs.Swimmer => new SwimmerBrain(spec.Parse<SwimmerParams>()),
        BrainSpecs.Lunger => new LungerBrain(spec.Parse<LungerParams>()),
        _ => throw new InvalidOperationException($"Unknown brain '{spec.Type}' (the content validator should have rejected it)."),
    };

    /// <summary>Jump velocity reaching <paramref name="cells"/> high under brain gravity.</summary>
    public static float JumpVelocity(float cells) => MathF.Sqrt(2f * BrainContext.Gravity * cells * Common.Units.CellSize);

    public static Vector3 Flat(Vector3 v)
    {
        var f = new Vector3(v.X, 0, v.Z);
        return f.LengthSquared() > 1e-6f ? Vector3.Normalize(f) : Vector3.Zero;
    }

    public static float YawOf(Vector3 dir) => MathF.Atan2(dir.X, dir.Z);
}

public sealed class IdleBrain : IBrain
{
    public bool UsesGravity => true;

    public void Think(Npc npc, BrainContext ctx)
    {
        npc.Velocity.X *= 0.8f;
        npc.Velocity.Z *= 0.8f;
        npc.Pose = "idle";
    }
}
