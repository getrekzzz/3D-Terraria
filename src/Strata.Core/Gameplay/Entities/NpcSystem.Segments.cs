using System.Numerics;
using Strata.Core.Content.Defs;
using Strata.Core.Gameplay.Combat;
using Strata.Core.Session;

namespace Strata.Core.Gameplay.Entities;

/// <summary>
/// Worms whose segments live on their own (the Eater of Worlds): each segment has its own life, defense and contact
/// damage; a killed body segment cuts the worm in two, each piece a worm of the same boss group; a killed head hands
/// its place to the next segment. A piece that would be a lone segment dies outright; the boss falls with its last piece.
/// </summary>
public sealed partial class NpcSystem
{
    private static bool SplitsSegments(Npc npc) => npc.Def.Boss is { SegmentLife.Length: > 0 } && npc.SegmentLife.Count > 0;

    /// <summary>Fills per-segment life at spawn (scaled like the creature's own life).</summary>
    private void InitSegmentLife(Npc npc)
    {
        if (npc.Def.Boss is not { SegmentLife.Length: >= 3 } boss || npc.Segments.Count == 0) return;
        float scale = MathF.Max(1f, Scale);
        npc.Life = npc.MaxLife = (int)(boss.SegmentLife[0] * scale);
        int n = npc.Segments.Count;
        for (int i = 0; i < n; i++) npc.SegmentLife.Add((int)(boss.SegmentLife[i == n - 1 ? 2 : 1] * scale));
        npc.WholeMaxLife = npc.TotalLife;
    }

    /// <summary>Segment kind for per-kind stats: 0 head (segment −1), 2 the last segment (tail), else 1 body.</summary>
    private static int KindOf(Npc npc, int segment) => segment < 0 ? 0 : segment == npc.Segments.Count - 1 ? 2 : 1;

    private static int SegmentDefense(Npc npc, int segment)
    {
        var table = npc.Def.Boss!.SegmentDefense;
        int kind = KindOf(npc, segment);
        if (kind >= table.Length) return npc.Defense;
        return npc.DefenseOverride ?? Math.Max(0, table[kind] + npc.Defense - npc.Def.Defense); // debuffs (ichor) still count
    }

    /// <summary>Contact damage of a touched volume (0 = head, i + 1 = segment i).</summary>
    private int ContactOf(Npc npc, int volume)
    {
        int full = ContactBase(npc);
        if (npc.Def.Boss is not { SegmentDamage.Length: >= 3 } boss || npc.Def.Damage <= 0) return full;
        int kind = KindOf(npc, volume - 1);
        return Math.Max(1, (int)MathF.Round(full * boss.SegmentDamage[kind] / (float)npc.Def.Damage));
    }

    private bool HitSegment(Npc npc, int segment, PlayerEntity? attacker, int baseDamage, DamageClass damageClass, int critChance, bool melee)
    {
        var hit = DamageModel.Roll(baseDamage, damageClass, critChance, 0f, attacker?.State.Effects.Stats, SegmentDefense(npc, segment),
            0.5f, 0f, 1f, ref _rng);
        npc.SegmentLife[segment] -= hit.Damage;
        npc.HurtFlash = 8;
        if (melee) npc.ImmuneTicks = 10;
        Damaged?.Invoke(new DamageEvent(npc.Segments[segment] + new Vector3(0, 0.9f, 0), hit.Damage, hit.Crit, false));
        if (attacker != null && damageClass == DamageClass.Magic && attacker.State.Effects.Stats.Bonus(Stats.StatIds.LifeSteal) is > 0 and var steal)
            attacker.State.Vitals.Heal(Math.Max(1, (int)(hit.Damage * steal)));
        if (npc.SegmentLife[segment] <= 0) Split(npc, segment);
        else if (melee && attacker != null)
            foreach (var inflict in attacker.State.Effects.Inflicts)
                if (_rng.Chance(inflict.Chance) && _world.Session.Content.Buffs.TryGet(inflict.Buff, out var buff)) AddBuff(npc, buff, inflict.Ticks);
        return true;
    }

    /// <summary>The creature's life ran out: a worm with segments left hands its head on; anything else is killed.</summary>
    private void Die(Npc npc)
    {
        if (SplitsSegments(npc) && npc.Segments.Count > 0) Promote(npc);
        else Kill(npc);
    }

    /// <summary>The head died: the first segment becomes the head (its life and place); a head left alone dies.</summary>
    private void Promote(Npc npc)
    {
        RollSegmentLoot(npc, npc.Center);
        npc.Position = npc.Segments[0] - new Vector3(0, npc.HalfExtents.Y, 0);
        npc.Life = npc.SegmentLife[0];
        npc.MaxLife = Math.Max(npc.MaxLife, npc.Life);
        RemoveSegments(npc, 0, 1);
        if (npc.Segments.Count == 0) Kill(npc);
    }

    /// <summary>
    /// Segment <paramref name="i"/> died. With <see cref="BossInfo.Splits"/> the segments behind it crawl off as a new worm
    /// (its head at segment i + 1); a lone tail behind the cut, or a head left with no body, dies outright. Without it the
    /// chain closes over the gap.
    /// </summary>
    internal void Split(Npc npc, int i)
    {
        RollSegmentLoot(npc, npc.Segments[i]);
        int n = npc.Segments.Count;
        if (!npc.Def.Boss!.Splits)
        {
            RemoveSegments(npc, i, 1);
            return;
        }
        if (i + 2 < n) SpawnPiece(npc, i + 1);
        else if (i + 1 < n) RollSegmentLoot(npc, npc.Segments[i + 1]);
        RemoveSegments(npc, i, n - i);
        if (npc.Segments.Count == 0) Kill(npc);
    }

    /// <summary>A new worm from segment <paramref name="from"/> (its head) to the tail; it joins the boss group silently.</summary>
    private void SpawnPiece(Npc src, int from)
    {
        // No creature cap here: the segments were already in the world (in Terraria each is its own NPC), and a worm
        // splits into at most half its segments, so the pieces may run a few past MaxNpcs rather than vanish.
        var head = src.Segments[from];
        var along = head - src.Segments[from + 1];
        var piece = new Npc(_nextId++, src.Def, head - new Vector3(0, src.HalfExtents.Y, 0))
        {
            Yaw = src.Yaw,
            Age = src.Age,
            ImmuneTicks = src.ImmuneTicks, // the swing that cut it does not strike the new piece again
            Life = src.SegmentLife[from],
            WholeMaxLife = src.WholeMaxLife,
        };
        piece.MaxLife = Math.Max(piece.Life, (int)(src.Def.Boss!.SegmentLife[1] * MathF.Max(1f, Scale)));
        piece.Velocity = along.LengthSquared() > 1e-4f ? Vector3.Normalize(along) * src.Velocity.Length() : src.Velocity;
        for (int k = from + 1; k < src.Segments.Count; k++)
        {
            piece.Segments.Add(src.Segments[k]);
            piece.SegmentLife.Add(src.SegmentLife[k]);
            if (k < src.SegmentFlags.Count) piece.SegmentFlags.Add(src.SegmentFlags[k]); // spent probes stay spent
        }
        _npcs.Add(piece);
        Spawned?.Invoke(piece);
    }

    private static void RemoveSegments(Npc npc, int index, int count)
    {
        npc.Segments.RemoveRange(index, count);
        npc.SegmentLife.RemoveRange(index, count);
        if (npc.SegmentFlags.Count > index) npc.SegmentFlags.RemoveRange(index, Math.Min(count, npc.SegmentFlags.Count - index));
    }

    /// <summary>Drops a dying segment's own loot (Shadow Scales, Demonite Ore) where it died.</summary>
    private void RollSegmentLoot(Npc npc, Vector3 at)
    {
        if (npc.Def.Boss?.SegmentLoot is not { } table) return;
        var cell = Physics.VoxelMotion.CellOf(at);
        foreach (var stack in _loot.Roll(table, ref _rng, _world.Session.ContextAt(cell)))
            _world.Drops.Spawn(stack, at, new Vector3(_rng.NextSigned() * 1.5f, 3f, _rng.NextSigned() * 1.5f));
    }

    /// <summary>Life left and life on arrival summed over a boss group's pieces (a split worm's boss bar).</summary>
    public (int Life, int Max) GroupLife(BossInfo boss)
    {
        int life = 0, max = 0;
        foreach (var n in _npcs)
        {
            if (!n.Alive || n.Def.Boss != boss) continue;
            life += n.TotalLife;
            max = Math.Max(max, n.WholeMaxLife > 0 ? n.WholeMaxLife : n.MaxLife);
        }
        return (life, max);
    }
}
