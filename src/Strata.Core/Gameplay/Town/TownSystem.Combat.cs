using System.Numerics;
using Strata.Core.AI;
using Strata.Core.Gameplay.Entities;

namespace Strata.Core.Gameplay.Town;

public sealed partial class TownSystem
{
    /// <summary>Ticks of immunity after a resident is hit.</summary>
    public const int HitImmunity = 30;

    /// <summary>A resident was slain (they return after <see cref="TownConfig.RespawnTicks"/>).</summary>
    public event Action<TownNpc>? Died;

    /// <summary>A resident took damage (numbers, flashes).</summary>
    public event Action<TownNpc, int>? Hurt;

    /// <summary>Hostile creatures touching a resident hurt them (defense halves the hit) and knock them back.</summary>
    private void TakeContactHits(TownNpc r)
    {
        if (r.ImmuneTicks > 0)
        {
            r.ImmuneTicks--;
            return;
        }
        var box = (r.Center - TownNpc.HalfExtents, r.Center + TownNpc.HalfExtents);
        foreach (var npc in _world.Npcs.All)
        {
            if (!npc.Alive || !npc.Def.ContactDamage || npc.Def.Damage <= 0) continue;
            if (Vector3.DistanceSquared(npc.Center, r.Center) > 64f || !npc.Volumes().Any(v => Npc.Overlaps(v, box))) continue;
            Damage(r, npc.Def.Damage, npc.Position);
            return;
        }
    }

    /// <summary>Applies a hit to a resident from a point (knockback away from it).</summary>
    public void Damage(TownNpc r, int amount, Vector3 from)
    {
        if (!r.Alive) return;
        int dealt = Math.Max(1, amount - r.Def.Defense / 2);
        r.Life -= dealt;
        r.ImmuneTicks = HitImmunity;
        var away = BrainFactory.Flat(r.Position - from);
        if (away != Vector3.Zero) r.Velocity += away * 4f + new Vector3(0, 4f, 0);
        Hurt?.Invoke(r, dealt);
        if (r.Life > 0) return;
        r.Life = 0;
        r.RespawnTicks = Config.RespawnTicks;
        r.Path = null;
        if (r.TalkingTo != null) EndTalk(r);
        Died?.Invoke(r);
    }
}
