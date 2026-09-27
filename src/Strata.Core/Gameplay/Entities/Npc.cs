using System.Numerics;
using Strata.Core.Common;
using Strata.Core.Content.Defs;
using Strata.Core.Gameplay.Buffs;

namespace Strata.Core.Gameplay.Entities;

/// <summary>A live creature. Positions are meters (feet centre); size comes from the definition (cells).</summary>
public sealed class Npc
{
    public Npc(long id, EnemyDef def, Vector3 feet)
    {
        Id = id;
        Def = def;
        Position = feet;
        Anchor = feet;
        Life = MaxLife = def.Life;
        HalfExtents = new Vector3(def.Width * 0.25f * def.Scale, def.Height * 0.25f * def.Scale, def.Width * 0.25f * def.Scale);
    }

    public long Id { get; }
    public EnemyDef Def { get; }
    public Vector3 Position { get; set; }
    public Vector3 Velocity;
    public Vector3 HalfExtents { get; }
    public int Life { get; set; }
    public int MaxLife { get; set; }
    public bool OnGround { get; set; }

    /// <summary>Horizontal facing (radians, 0 = +Z).</summary>
    public float Yaw { get; set; }

    public int Age { get; set; }

    /// <summary>Melee immunity after a hit (ticks).</summary>
    public int ImmuneTicks { get; set; }

    /// <summary>Ticks left of the hurt flash (visual).</summary>
    public int HurtFlash { get; set; }

    /// <summary>Ticks left reeling from a hit: the brain does not steer, so knockback carries it away.</summary>
    public int StunTicks { get; set; }

    /// <summary>Ticks spent far from every player (despawn timer).</summary>
    public int FarTicks { get; set; }

    public BuffSet Buffs { get; } = new();

    /// <summary>Effects of active buffs (damage over time, light), rebuilt when buffs change.</summary>
    public Stats.EffectState Effects { get; } = new();

    /// <summary>Defense after buffs and debuffs (ichor, ironbark-like effects), never below zero.</summary>
    public int Defense => Math.Max(0, (int)MathF.Round(Effects.Stats.Get(Stats.StatIds.Defense, Def.Defense)));

    public void RecomputeEffects()
    {
        Effects.Clear();
        foreach (var b in Buffs.Active)
            foreach (var e in b.Def.Effects) Effects.Apply(e);
    }

    // Brain scratch state (meaning defined by the brain).
    /// <summary>Multiplier on contact damage (bosses enraged by daylight).</summary>
    public float DamageScale { get; set; } = 1f;

    /// <summary>Set by the brain in a creature's second phase (or day rage); switches to <see cref="Content.Defs.RangedAttack.Enraged"/>.</summary>
    public bool Enraged { get; set; }

    /// <summary>Ticks until the next ranged volley.</summary>
    public int AttackTimer;

    /// <summary>Where the creature spawned (anchored creatures stay near it).</summary>
    public Vector3 Anchor;

    public int Timer;

    /// <summary>Pattern bosses: the phase entered (−1 before the first) and volley/summon clocks.</summary>
    public int Stage = -1;

    public int VolleyTimer;
    public int SummonTimer;
    public int Counter;
    public int Counter2;
    public int Phase;
    public Vector3 Direction;

    /// <summary>Set by the brain: it has given up (dawn) and is walking or flying away; it despawns once out of sight.</summary>
    public bool Leaving { get; set; }

    /// <summary>Set by the brain: the player it is chasing (null when idle/wandering).</summary>
    public long TargetPlayer { get; set; } = -1;

    /// <summary>What the visual should show ("idle", "move", "attack", "jump", "telegraph", "teleport").</summary>
    public string Pose { get; set; } = "idle";

    /// <summary>Model shown instead of the definition's (boss phase changes).</summary>
    public string? ModelOverride { get; set; }

    /// <summary>Temporarily immune to damage (teleporting, burrowing phases).</summary>
    public bool Invulnerable { get; set; }

    /// <summary>The creature this one is tethered to (a boss's hungry mouths): its anchor follows the leader and it dies with it.</summary>
    public Npc? Leader { get; set; }

    /// <summary>Anchor offset from the leader's centre.</summary>
    public Vector3 LeaderOffset { get; set; }

    /// <summary>The offset is in the leader's own frame and turns with its facing (a boss's hands).</summary>
    public bool TurnsWithLeader { get; set; }

    /// <summary>Live creatures tethered to this one.</summary>
    public List<Npc> Followers { get; } = new();

    /// <summary>
    /// A wall this creature is (the Mawwall's flesh): an axis-aligned box sweeping along <see cref="Direction"/>.X.
    /// Players inside it or behind its front face are hurt and thrown out in front. Set by the brain.
    /// </summary>
    public (Vector3 Min, Vector3 Max)? Barrier { get; set; }

    /// <summary>Which body part fires the next ranged volley (ranged attacks from parts).</summary>
    public int PartCursor { get; set; }

    /// <summary>Taken out of the simulation (killed or despawned).</summary>
    public bool Removed { get; internal set; }

    /// <summary>Centres of trailing body segments (worms), head first; empty for single-body creatures.</summary>
    public List<Vector3> Segments { get; } = new();

    public bool IsBoss => Def.Boss is { Minor: false };

    /// <summary>Every volume that can hit or be hit: the body plus segments.</summary>
    public IEnumerable<(Vector3 Min, Vector3 Max)> Volumes()
    {
        yield return Box;
        if (Segments.Count == 0) yield break;
        var half = new Vector3(Def.Boss?.SegmentRadius ?? 0.5f);
        foreach (var s in Segments) yield return (s - half, s + half);
    }

    public bool Alive => Life > 0;
    public Vector3 Center => Position + new Vector3(0, HalfExtents.Y, 0);
    public (Vector3 Min, Vector3 Max) Box => (Center - HalfExtents, Center + HalfExtents);

    public Int3 Cell => new((int)MathF.Floor(Position.X / Units.CellSize), (int)MathF.Floor((Position.Y + 0.1f) / Units.CellSize), (int)MathF.Floor(Position.Z / Units.CellSize));

    public static bool Overlaps((Vector3 Min, Vector3 Max) a, (Vector3 Min, Vector3 Max) b) =>
        a.Min.X < b.Max.X && a.Max.X > b.Min.X && a.Min.Y < b.Max.Y && a.Max.Y > b.Min.Y && a.Min.Z < b.Max.Z && a.Max.Z > b.Min.Z;
}
