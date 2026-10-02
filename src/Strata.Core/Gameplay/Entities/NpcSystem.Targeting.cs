using System.Numerics;
using Strata.Core.Gameplay.Combat;
using Strata.Core.Session;

namespace Strata.Core.Gameplay.Entities;

/// <summary>
/// Town residents as targets (Terraria): ordinary hostile creatures go after the nearest counting resident within
/// <see cref="SpawnConfig.ResidentAggroRange"/> when it is nearer than the player by <see cref="SpawnConfig.PlayerPreference"/>.
/// Bosses and their parts, critters, passive, harmless or invulnerable creatures and disguised ones only care about
/// players. The choice is re-made every <see cref="RetargetInterval"/> ticks.
/// </summary>
public sealed partial class NpcSystem
{
    public const int RetargetInterval = 15;

    private ICombatTarget? ChooseTarget(Npc npc, PlayerEntity? player, float playerDistance, out float distance)
    {
        distance = playerDistance;
        var def = npc.Def;
        if (def.Boss != null || npc.IsPart || def.Invulnerable || def.Damage <= 0 || npc.Disguised || def.HasTag("critter") || def.HasTag("passive"))
            return player;
        var cfg = _world.Spawner.Config;
        if (--npc.RetargetTicks <= 0)
        {
            npc.RetargetTicks = RetargetInterval;
            npc.ResidentTarget = null;
            float best = MathF.Min(cfg.ResidentAggroRange, playerDistance - cfg.PlayerPreference);
            foreach (var r in _world.Town.Residents)
            {
                if (!r.CountsAsResident) continue;
                float d = Vector3.Distance(npc.Center, r.Center);
                if (d >= best) continue;
                best = d;
                npc.ResidentTarget = r;
            }
        }
        if (npc.ResidentTarget is not { CountsAsResident: true } target) return player;
        distance = Vector3.Distance(npc.Center, target.Center);
        return target;
    }
}
