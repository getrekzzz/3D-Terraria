using System.Numerics;
using Strata.Core.AI;
using Strata.Core.Content.Defs;
using Strata.Core.Gameplay.Combat;
using Strata.Core.Gameplay.Entities;

namespace Strata.Core.Gameplay.Town;

/// <summary>
/// Residents fight back (Terraria's town NPC attacks): every <see cref="DefenseStagger"/> ticks (staggered by id) a
/// free resident looks for the nearest hostile creature in sight within its attack range. With a
/// <see cref="TownAttack"/> it faces the threat, stops briefly and throws, shoots, casts or strikes; the Nurse heals
/// the most hurt resident nearby and the Dryad wards everyone around while a threat is about. Residents
/// without an attack run from threats within <see cref="FleeRange"/>. Shots are friendly projectiles without an
/// owner, so they hit only creatures (never players, residents or terrain, see the validator).
/// </summary>
public sealed partial class TownSystem
{
    public const int DefenseStagger = 6;
    public const int AttackPause = 20;
    public const int FleeDuration = 60;
    public const float FleeRange = 5f;
    public const float MeleeReach = 1.8f;

    /// <summary>A resident attacked, healed or warded (animation, sound).</summary>
    public event Action<TownNpc, TownAttack>? ResidentAttacked;

    /// <summary>A resident was healed by another (numbers).</summary>
    public event Action<TownNpc, int>? ResidentHealed;

    private bool Hardmode => _world.Session.Flags.Contains("hardmode");

    private void TickDefense(TownNpc r)
    {
        if (r.AttackCooldown > 0) r.AttackCooldown--;
        if (r.AttackTicks > 0) r.AttackTicks--;
        if (r.FleeTicks > 0) r.FleeTicks--;
        if (r.WardTicks > 0) r.WardTicks--;
        if ((_world.Session.Clock.Tick + r.Id) % DefenseStagger != 0) return;
        var attack = r.Def.Attack;
        r.Threat = NearestThreat(r, attack?.Range ?? FleeRange);
        if (attack == null)
        {
            if (r.Threat == null) return;
            r.FleeTicks = FleeDuration;
            r.Path = null;
            r.FleeFrom = r.Threat.Center;
            return;
        }
        if (r.AttackCooldown > 0) return;
        if (attack.Style != "heal" && r.Threat == null) return;
        if (!Act(r, attack)) return;
        r.AttackCooldown = attack.Interval;
        r.Attacks++;
        ResidentAttacked?.Invoke(r, attack);
    }

    /// <summary>Clears a resident's fight state (death, binding).</summary>
    private static void ClearDefense(TownNpc r)
    {
        r.Threat = null;
        r.AttackTicks = r.FleeTicks = r.WardTicks = 0;
    }

    /// <summary>The nearest live hostile creature within range that the resident can see (critters, passive and disguised ones are ignored).</summary>
    private Npc? NearestThreat(TownNpc r, float range)
    {
        Npc? best = null;
        float bestSq = range * range;
        var eye = r.Eye;
        var all = _world.Npcs.All;
        for (int i = 0; i < all.Count; i++)
        {
            var n = all[i];
            if (!n.Alive || n.Disguised || n.Def.Invulnerable || n.Def.Damage <= 0 || n.Def.HasTag("critter") || n.Def.HasTag("passive")) continue;
            float d = Vector3.DistanceSquared(n.Center, r.Center);
            if (d >= bestSq || !_world.Npcs.InSight(eye, n.Center)) continue;
            bestSq = d;
            best = n;
        }
        return best;
    }

    private bool Act(TownNpc r, TownAttack a)
    {
        float scale = Hardmode ? a.HardmodeMultiplier : 1f;
        int damage = (int)MathF.Round(a.Damage * scale);
        switch (a.Style)
        {
            case "heal":
                return HealNearby(r, a);
            case "ward":
                Ward(r, a);
                Face(r, r.Threat!.Center);
                return true;
            case "melee":
                var t = r.Threat!;
                if (Vector3.Distance(t.Center, r.Center) > MeleeReach + t.HalfExtents.X) return false;
                Face(r, t.Center);
                _world.Npcs.Hit(t, null, damage, DamageClass.Melee, 0, a.Knockback, r.Center, melee: true);
                return true;
            default:
                if (a.Projectile == null || !_world.Session.Content.Projectiles.TryGet(a.Projectile, out var def)) return false;
                var target = r.Threat!.Center;
                var from = r.Eye - new Vector3(0, 0.2f, 0);
                var dir = Ballistics.Aim(from, target, a.Speed, def.Gravity);
                float spread = a.Spread * MathF.PI / 180f;
                var cls = a.Style == "magic" ? DamageClass.Magic : DamageClass.Ranged;
                for (int i = 0; i < a.Count; i++)
                {
                    var d = dir;
                    if (spread > 0)
                    {
                        var side = Vector3.Normalize(Vector3.Cross(d, Vector3.UnitY) + new Vector3(1e-4f, 0, 0));
                        d = Vector3.Normalize(d + side * _rng.NextSigned() * spread + Vector3.UnitY * _rng.NextSigned() * spread * 0.5f);
                    }
                    _world.Projectiles.Spawn(def, from + d * 0.5f, d * a.Speed, null, damage, a.Knockback, cls, 0);
                }
                Face(r, target);
                return true;
        }
    }

    /// <summary>Faces a point and stands still for the attack's moment.</summary>
    private static void Face(TownNpc r, Vector3 at)
    {
        r.Yaw = BrainFactory.YawOf(at - r.Position);
        r.AttackTicks = AttackPause;
    }

    /// <summary>
    /// Heals the most hurt (by fraction) resident within range; false when nobody needs it. As in Terraria the Nurse
    /// mends other townsfolk for free, while players pay for her healing in conversation.
    /// </summary>
    private bool HealNearby(TownNpc healer, TownAttack a)
    {
        float worst = 1f, rangeSq = a.Range * a.Range;
        TownNpc? resident = null;
        foreach (var o in _residents)
        {
            if (!o.Alive || o.Bound || o.Life >= o.Def.Life || Vector3.DistanceSquared(o.Center, healer.Center) > rangeSq) continue;
            float f = o.Life / (float)o.Def.Life;
            if (f < worst) (worst, resident) = (f, o);
        }
        if (resident == null) return false;
        int before = resident.Life;
        resident.Life = Math.Min(resident.Def.Life, resident.Life + a.Heal);
        ResidentHealed?.Invoke(resident, resident.Life - before);
        Face(healer, resident.Center);
        return true;
    }

    /// <summary>Gives the ward buff to players in range and extra defense to residents in range (the warder included).</summary>
    private void Ward(TownNpc r, TownAttack a)
    {
        float rangeSq = a.Range * a.Range;
        if (a.Buff != null && _world.Session.Content.Buffs.TryGet(a.Buff, out var buff))
            foreach (var p in _world.Players)
                if (p.Alive && Vector3.DistanceSquared(p.Center, r.Center) <= rangeSq)
                    p.State.Buffs.Add(buff, a.BuffTicks, p.State.Effects.Immunities);
        foreach (var o in _residents)
            if (o.Alive && !o.Bound && Vector3.DistanceSquared(o.Center, r.Center) <= rangeSq)
            {
                o.WardTicks = Math.Max(o.WardTicks, a.BuffTicks);
                o.WardDefense = a.WardDefense;
            }
    }

    /// <summary>The flight wish of a resident running from a threat (zero when not fleeing).</summary>
    private Vector3 FleeWish(TownNpc r)
    {
        var away = BrainFactory.Flat(r.Position - r.FleeFrom);
        return away == Vector3.Zero ? Vector3.Zero : away * r.Def.Speed * 1.4f;
    }
}
