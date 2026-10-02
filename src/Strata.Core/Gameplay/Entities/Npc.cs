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
        Disguised = def.Disguise != null;
        BaseHalfExtents = HalfExtents = new Vector3(def.Width * 0.25f * def.Scale, def.Height * 0.25f * def.Scale, def.Width * 0.25f * def.Scale);
    }

    public long Id { get; }
    public EnemyDef Def { get; }
    public Vector3 Position { get; set; }
    public Vector3 Velocity;
    /// <summary>Half the collision box (m); a brain may shrink it from <see cref="BaseHalfExtents"/> (King Slime losing gel).</summary>
    public Vector3 HalfExtents { get; set; }

    /// <summary>Half the box the definition gives.</summary>
    public Vector3 BaseHalfExtents { get; }
    public int Life { get; set; }
    public int MaxLife { get; set; }
    public bool OnGround { get; set; }

    /// <summary>Downward speed (m/s) with which it came to rest on the ground on its last move; 0 when it did not land.</summary>
    public float LandingSpeed { get; set; }

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

    /// <summary>
    /// Defense after buffs and debuffs (ichor, ironbark-like effects) and the brain's <see cref="DefenseDelta"/>, never
    /// below zero; <see cref="DefenseOverride"/> (an enrage's lethal defense) replaces it outright.
    /// </summary>
    public int Defense => DefenseOverride ?? Math.Max(0, (int)MathF.Round(Effects.Stats.Get(Stats.StatIds.Defense, Def.Defense)) + DefenseDelta);

    /// <summary>Defense the brain adds this tick (a guarding phase); zeroed before each think.</summary>
    public int DefenseDelta { get; set; }

    /// <summary>Fixed defense while set (a pattern phase's, or the enrage step's); cleared before each think.</summary>
    public int? DefenseOverride { get; set; }

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

    /// <summary>Multiplier on how far the creature moves each tick (enraged speed); 1 = normal.</summary>
    public float SpeedScale { get; set; } = 1f;

    /// <summary>Presentation only: scale of the model (a boss swelling in a phase).</summary>
    public float VisualScale { get; set; } = 1f;

    /// <summary>Presentation only: the model is mirrored (a left hand).</summary>
    public bool Mirrored { get; set; }

    /// <summary>Its <see cref="Content.Defs.BossInfo.Enrage"/> conditions hold (or held, when latched).</summary>
    public bool Frenzied { get; set; }

    /// <summary>Presentation: decoy copies shown around the creature.</summary>
    public int Illusions { get; set; }

    /// <summary>Presentation: the point illusions are mirrored around.</summary>
    public Vector3 MirrorAround { get; set; }

    /// <summary>Ticks until the next ranged volley.</summary>
    public int AttackTimer;

    /// <summary>Where the creature spawned (anchored creatures stay near it).</summary>
    public Vector3 Anchor;

    public int Timer;

    /// <summary>Pattern bosses: the phase entered (−1 before the first) and volley/summon clocks.</summary>
    public int Stage = -1;

    public int VolleyTimer;
    public int SummonTimer;

    /// <summary>Pattern bosses: ticks into the current phase.</summary>
    public int PhaseTimer;
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

    /// <summary>Still posing as its <see cref="EnemyDef.Disguise"/> (a chest, a lost girl): harmless and still until woken.</summary>
    public bool Disguised { get; set; }

    /// <summary>The model to draw: the disguise while disguised, else a phase's override, else the creature's own.</summary>
    public string ShownModel => Disguised ? Def.Disguise!.Model : ModelOverride ?? Def.Model ?? Def.Id;

    /// <summary>Temporarily immune to damage (teleporting, burrowing phases).</summary>
    public bool Invulnerable { get; set; }

    /// <summary>Released by a wired statue: drops no loot or coins and counts towards the statue caps.</summary>
    public bool FromStatue { get; set; }

    /// <summary>The player who last struck it (Hearts and Stars drop for them), and whether that hit was magic.</summary>
    public Session.PlayerEntity? LastAttacker { get; set; }

    public bool LastHitMagic { get; set; }

    /// <summary>The creature this one is tethered to (a boss's hungry mouths): its anchor follows the leader and it dies with it.</summary>
    public Npc? Leader { get; set; }

    /// <summary>
    /// A creature this one lives and dies with but does not follow (Golem's freed head): it is removed, without loot,
    /// once that creature dies or goes.
    /// </summary>
    public Npc? BoundTo { get; set; }

    /// <summary>Anchor offset from the leader's centre.</summary>
    public Vector3 LeaderOffset { get; set; }

    /// <summary>The offset is in the leader's own frame and turns with its facing (a boss's hands).</summary>
    public bool TurnsWithLeader { get; set; }

    /// <summary>The boss part this creature was spawned as (null for anything else).</summary>
    public BossPart? Part { get; set; }

    /// <summary>A fixed part of a boss body (Moon Lord's hands): it stays and goes with its leader.</summary>
    public bool IsPart => Leader != null && TurnsWithLeader;

    /// <summary>Its life counts on its leader's boss bar.</summary>
    public bool InBossBar => Part?.InBossBar ?? false;

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

    /// <summary>Life left of each segment, parallel to <see cref="Segments"/> (worms whose segments die on their own); else empty.</summary>
    public List<int> SegmentLife { get; } = new();

    /// <summary>Per-segment state bits for brains (parallel to <see cref="Segments"/> when used).</summary>
    public List<byte> SegmentFlags { get; } = new();

    /// <summary>Life of the whole piece: the head plus every segment's.</summary>
    public int TotalLife
    {
        get
        {
            int sum = Life;
            foreach (int l in SegmentLife) sum += l;
            return sum;
        }
    }

    /// <summary>The whole worm's life when it arrived (every piece cut from it keeps it), for a boss bar.</summary>
    public int WholeMaxLife { get; set; }

    public bool IsBoss => Def.Boss is { Minor: false };

    /// <summary>Every volume that can hit or be hit: the body plus segments (allocates an iterator; see <see cref="VolumeAt"/>).</summary>
    public IEnumerable<(Vector3 Min, Vector3 Max)> Volumes()
    {
        for (int i = 0; i < VolumeCount; i++) yield return VolumeAt(i);
    }

    /// <summary>How many volumes <see cref="VolumeAt"/> serves: the body plus one per segment.</summary>
    public int VolumeCount => 1 + Segments.Count;

    /// <summary>Volume <paramref name="i"/> without allocating: 0 is the body's <see cref="Box"/>, then each segment's cube.</summary>
    public (Vector3 Min, Vector3 Max) VolumeAt(int i)
    {
        if (i == 0) return Box;
        var half = new Vector3(Def.Boss?.SegmentRadius ?? 0.5f);
        var s = Segments[i - 1];
        return (s - half, s + half);
    }

    /// <summary>The first volume passing a test: −1 none, 0 the body (head), i + 1 segment i. Allocation-free walk.</summary>
    public int FirstVolume(Func<Vector3, Vector3, bool> test)
    {
        var box = Box;
        if (test(box.Min, box.Max)) return 0;
        if (Segments.Count == 0) return -1;
        var half = new Vector3(Def.Boss?.SegmentRadius ?? 0.5f);
        for (int i = 0; i < Segments.Count; i++)
            if (test(Segments[i] - half, Segments[i] + half)) return i + 1;
        return -1;
    }

    public bool Alive => Life > 0;
    public Vector3 Center => Position + new Vector3(0, HalfExtents.Y, 0);
    public (Vector3 Min, Vector3 Max) Box => (Center - HalfExtents, Center + HalfExtents);

    public Int3 Cell => new((int)MathF.Floor(Position.X / Units.CellSize), (int)MathF.Floor((Position.Y + 0.1f) / Units.CellSize), (int)MathF.Floor(Position.Z / Units.CellSize));

    public static bool Overlaps((Vector3 Min, Vector3 Max) a, (Vector3 Min, Vector3 Max) b) =>
        a.Min.X < b.Max.X && a.Max.X > b.Min.X && a.Min.Y < b.Max.Y && a.Max.Y > b.Min.Y && a.Min.Z < b.Max.Z && a.Max.Z > b.Min.Z;
}
