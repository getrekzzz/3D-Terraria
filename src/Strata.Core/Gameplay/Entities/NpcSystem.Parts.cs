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
        npc.Anchor = leader.Center + npc.LeaderOffset;
        return true;
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
