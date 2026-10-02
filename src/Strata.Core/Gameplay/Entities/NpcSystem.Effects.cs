using System.Numerics;
using Strata.Core.Common;
using Strata.Core.Content.Specs;
using Strata.Core.Session;

namespace Strata.Core.Gameplay.Entities;

public sealed partial class NpcSystem
{
    /// <summary>Fires the creature's ranged volley at its target when in range, in sight and off cooldown.</summary>
    private void RangedAttack(Npc npc, PlayerEntity target, float distance)
    {
        var r = npc.Def.Ranged!;
        if (npc.Enraged && r.Enraged != null) r = r.Enraged;
        if (npc.AttackTimer > 0)
        {
            npc.AttackTimer--;
            return;
        }
        var baseAttack = npc.Def.Ranged!;
        bool parts = baseAttack.FromParts && npc.Segments.Count > 0;
        int part = !parts ? -1 : baseAttack.Parts.Length > 0
            ? baseAttack.Parts[npc.PartCursor % baseAttack.Parts.Length] % npc.Segments.Count
            : NextLitSegment(npc);
        if (parts && part < 0) return; // every plate has launched its probe: the worm has gone dark
        var from = parts ? npc.Segments[part] : npc.Center + new Vector3(0, npc.HalfExtents.Y * 0.4f, 0);
        if (!target.Alive || Vector3.Distance(from, target.Center) > r.Range || !InSight(from, target.Eye)) return;
        if (!_world.Session.Content.Projectiles.TryGet(r.Projectile, out var def)) return;
        npc.AttackTimer = r.IntervalAtZeroLife is int fastest && npc.MaxLife > 0
            ? (int)MathF.Round(r.Interval + (fastest - r.Interval) * (1f - npc.Life / (float)npc.MaxLife))
            : r.Interval;
        if (parts) npc.PartCursor++;
        var to = target.Center + new Vector3(0, 0.3f, 0);
        float flight = Vector3.Distance(from, to) / r.Speed;
        to.Y += 0.5f * def.Gravity * flight * flight; // lob gravity-bound projectiles
        var dir = Vector3.Normalize(to - from);
        float spread = r.Spread * MathF.PI / 180f;
        for (int i = 0; i < r.Count; i++)
        {
            var d = dir;
            if (spread > 0)
            {
                var side = Vector3.Normalize(Vector3.Cross(d, Vector3.UnitY) + new Vector3(1e-4f, 0, 0));
                var up = Vector3.Cross(side, d);
                d = Vector3.Normalize(d + side * _rng.NextSigned() * spread + up * _rng.NextSigned() * spread);
            }
            float clear = parts ? (npc.Def.Boss?.SegmentRadius ?? 0.5f) + 0.2f : npc.HalfExtents.X + 0.2f;
            _world.Projectiles.Spawn(def, from + d * clear, d * r.Speed, null, (int)(r.Damage * Scale), 3f, Content.Defs.DamageClass.None, 0);
        }
        npc.Pose = "attack";
    }

    /// <summary>
    /// The segment the part cursor points at, skipping segments that cannot fire (a Destroyer plate whose probe is spent
    /// goes dark, as in Terraria); the cursor moves on to it. −1 when every segment is dark. A bounded allocation-free walk.
    /// </summary>
    private static int NextLitSegment(Npc npc)
    {
        int n = npc.Segments.Count;
        for (int k = 0; k < n; k++)
        {
            int i = (npc.PartCursor + k) % n;
            if (!AI.WormBrain.Fires(npc, i)) continue;
            npc.PartCursor += k;
            return i;
        }
        return -1;
    }

    /// <summary>A brain drags a player this tick (the tongue); cleared at the start of every tick.</summary>
    private static void Pull(PlayerEntity player, Vector3 velocity) => player.ExternalVelocity = velocity;

    /// <summary>No solid cell between two points (sampled every quarter metre).</summary>
    public bool InSight(Vector3 a, Vector3 b)
    {
        var world = _world.Session.World;
        float len = Vector3.Distance(a, b);
        int steps = Math.Max(1, (int)(len / 0.25f));
        for (int i = 1; i < steps; i++)
        {
            var p = Vector3.Lerp(a, b, i / (float)steps);
            if (world.IsSolid((int)MathF.Floor(p.X / Units.CellSize), (int)MathF.Floor(p.Y / Units.CellSize), (int)MathF.Floor(p.Z / Units.CellSize))) return false;
        }
        return true;
    }

    private void ApplyContactEffects(Npc npc, PlayerEntity player)
    {
        foreach (var fx in npc.Def.OnContact)
        {
            if (fx.Type != CreatureEffectSpecs.ApplyBuff) continue;
            var b = fx.Parse<ApplyBuffParams>();
            if (!_rng.Chance(b.Chance) || !_world.Session.Content.Buffs.TryGet(b.Buff, out var buff)) continue;
            player.State.Buffs.Add(buff, b.Ticks, player.State.Effects.Immunities);
            player.State.Recompute();
        }
    }
}
