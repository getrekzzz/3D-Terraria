using System.Numerics;
using Strata.Core.Content.Specs;
using Strata.Core.Determinism;
using Strata.Core.Gameplay.Combat;
using Strata.Core.Gameplay.Entities;
using Strata.Core.Session;
using Strata.Core.Voxels;

namespace Strata.Core.AI;

/// <summary>What a brain may look at when deciding (the world, the nearest player, time, randomness).</summary>
public sealed class BrainContext
{
    public required VoxelWorld World { get; init; }
    /// <summary>The nearest living thing to fight (a player today); null when there is none.</summary>
    public ICombatTarget? Target { get; set; }

    /// <summary>The target when it is a player, for player-only effects (confusion, the tongue, buffs).</summary>
    public PlayerEntity? TargetPlayer => Target as PlayerEntity;

    public float TargetDistance { get; set; }
    public bool IsDay { get; set; }
    public bool OnSurface { get; set; }

    /// <summary>Expert or Master world (never Journey or Classic): Expert-only attacks and phases.</summary>
    public bool Expert { get; set; }

    public Pcg32 Rng;

    /// <summary>Spawns a minion (bosses); returns null when the creature cap is reached.</summary>
    public Func<Content.Defs.EnemyDef, Vector3, Npc?>? Spawn { get; set; }

    /// <summary>How many living creatures of a kind exist (caps a boss's minions).</summary>
    public Func<string, int>? CountAlive { get; set; }

    /// <summary>Feet position on standable ground in the column under or over a point (loaded chunks), or null.</summary>
    public Func<Vector3, Vector3?>? Ground { get; set; }

    /// <summary>Fires a hostile projectile (id, from, velocity, damage) — pattern bosses' volleys.</summary>
    public Action<string, Vector3, Vector3, int>? Shoot { get; set; }

    /// <summary>Announces a message to every player (a boss changing phase).</summary>
    public Action<string>? Announce { get; set; }

    /// <summary>A heavy landing or slam at a point with a strength 0–1 (camera shake, dust): raises NpcSystem.Impacted.</summary>
    public Action<Vector3, float>? Impact { get; set; }

    /// <summary>No solid cell between two points.</summary>
    public Func<Vector3, Vector3, bool>? InSight { get; set; }

    /// <summary>Plays a sound cue at a creature (a roar, a telegraph): raises NpcSystem.Cued.</summary>
    public Action<Npc, string>? Cue { get; set; }

    /// <summary>Gives a buff (id, ticks) to every living player within a radius of a point, honouring their immunities.</summary>
    public Action<Vector3, float, string, int>? Pulse { get; set; }

    /// <summary>Removes natural terrain in a sphere (radius in cells) — burrowing creatures.</summary>
    public Action<Vector3, float>? Carve { get; set; }

    public required Content.ContentDatabase Content { get; init; }

    /// <summary>The world's layer heights (cells): underworld cavity, lava level, surface…</summary>
    public required WorldGen.Plan.LayerBands Bands { get; init; }

    public const float Gravity = 22f;
}

/// <summary>Per-tick decision making for a creature: writes its velocity, facing and pose.</summary>
public interface IBrain
{
    /// <summary>Whether gravity applies to this creature.</summary>
    bool UsesGravity { get; }

    /// <summary>Whether gravity applies to this creature right now (it can change by phase).</summary>
    bool UsesGravityFor(Npc npc) => UsesGravity;

    /// <summary>Moves through terrain without collision (burrowers); the brain handles everything.</summary>
    bool MovesFreely => false;

    /// <summary>Places body segments itself (free parts like hands) instead of dragging them as a chain.</summary>
    bool PositionsSegments => false;

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
        BrainSpecs.CryptWarden => new CryptWardenBrain(spec.Parse<CryptWardenParams>()),
        BrainSpecs.Hand => new HandBrain(spec.Parse<HandParams>()),
        BrainSpecs.Caster => new CasterBrain(spec.Parse<CasterParams>()),
        BrainSpecs.Mawwall => new MawwallBrain(spec.Parse<MawwallParams>()),
        BrainSpecs.Pattern => new PatternBrain(spec.Parse<PatternParams>()),
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
