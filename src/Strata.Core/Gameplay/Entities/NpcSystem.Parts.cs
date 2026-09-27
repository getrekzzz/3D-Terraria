using System.Numerics;
using Strata.Core.Gameplay.Combat;
using Strata.Core.Session;

namespace Strata.Core.Gameplay.Entities;

/// <summary>Creatures made of more than one thing: tethered followers and walls that sweep the world.</summary>
public sealed partial class NpcSystem
{
    /// <summary>Knockback (m/s) that throws a player out in front of a barrier.</summary>
    private const float BarrierThrow = 16f;

    /// <summary>Keeps a tethered follower's anchor on its leader; false when the leader is gone (it goes too).</summary>
    private static bool FollowLeader(Npc npc)
    {
        if (npc.Leader is not { } leader) return true;
        if (!leader.Alive || leader.Removed) return false;
        var o = npc.LeaderOffset;
        if (npc.TurnsWithLeader)
        {
            float c = MathF.Cos(leader.Yaw), s = MathF.Sin(leader.Yaw);
            o = new Vector3(o.X * c + o.Z * s, o.Y, -o.X * s + o.Z * c);
        }
        npc.Anchor = leader.Center + o;
        return true;
    }

    /// <summary>Spawns a boss's fixed parts around it (their offsets turn with it).</summary>
    private void SpawnParts(Npc npc)
    {
        foreach (var part in npc.Def.Boss!.Parts)
        {
            if (!_world.Session.Content.Enemies.TryGet(part.Enemy, out var def)) continue;
            var o = new Vector3(part.Offset.ElementAtOrDefault(0), part.Offset.ElementAtOrDefault(1), part.Offset.ElementAtOrDefault(2));
            if (Spawn(def, npc.Center + o - new Vector3(0, def.Height * 0.25f * def.Scale, 0)) is not { } m) continue;
            Tether(m, npc, o);
            m.TurnsWithLeader = true;
            FollowLeader(m);
        }
    }

    /// <summary>Kills counted towards a shielded creature's barrier (kept in the world's counters).</summary>
    public int ShieldKills(Content.Defs.EnemyDef def) => _world.Session.Counters.GetValueOrDefault("shield:" + def.Id);

    public bool ShieldUp(Npc npc) => npc.Def.Shield is { } sh && ShieldKills(npc.Def) < sh.Kills;

    /// <summary>A creature died: it counts towards the barrier of every living shielded creature of its kind.</summary>
    private void CountShieldKill(Npc dead)
    {
        foreach (var n in _npcs)
        {
            if (n == dead || !n.Alive || n.Def.Shield is not { } sh || !dead.Def.HasTag(sh.KillTag)) continue;
            var key = "shield:" + n.Def.Id;
            int k = _world.Session.Counters.GetValueOrDefault(key);
            if (k >= sh.Kills) continue;
            _world.Session.Counters[key] = k + 1;
            if (k + 1 == sh.Kills) Announced?.Invoke($"The {n.Def.DisplayName}'s shield is down!");
        }
    }

    /// <summary>Tethers a spawned minion to its leader (the offset is kept from the leader's centre).</summary>
    public static void Tether(Npc minion, Npc leader, Vector3 offset)
    {
        minion.Leader = leader;
        minion.LeaderOffset = offset;
        minion.Anchor = leader.Center + offset;
        leader.Followers.Add(minion);
    }

    /// <summary>
    /// Players inside the wall, or who slipped behind its front face within its height and width, take its damage and
    /// are thrown out in front of it (the wall cannot be walked around or hidden behind).
    /// </summary>
    private void BarrierEffects(Npc npc)
    {
        var (min, max) = npc.Barrier!.Value;
        float dir = npc.Direction.X >= 0 ? 1f : -1f;
        float front = dir > 0 ? max.X : min.X;
        foreach (var p in _world.Players)
        {
            if (!p.Alive) continue;
            var c = p.Center;
            if (c.Y < min.Y || c.Y > max.Y || c.Z < min.Z || c.Z > max.Z) continue;
            if ((c.X - front) * dir > -PlayerEntity.HalfExtents.X) continue; // in front of the wall
            var state = p.State;
            var hit = DamageModel.Roll((int)(npc.Def.Damage * npc.DamageScale), Content.Defs.DamageClass.None, 0, 0f, null, state.Defense,
                DamageModel.DefenseFactor(_world.Session.Settings.Difficulty), state.Effects.Stats.Bonus(Stats.StatIds.EnduranceReduction), 1f, ref _rng);
            if (!state.Vitals.Hurt(hit.Damage, PlayerHitImmunity)) continue;
            ReportPlayerDamage(p, hit.Damage, new Vector3(dir * BarrierThrow, 5f, 0));
        }
    }
}
