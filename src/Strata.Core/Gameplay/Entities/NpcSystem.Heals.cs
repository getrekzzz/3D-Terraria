using System.Numerics;
using Strata.Core.Determinism;
using Strata.Core.Session;

namespace Strata.Core.Gameplay.Entities;

/// <summary>Hearts and mana Stars from kills (P3-29), after Terraria 1.4.4's NPCLoot_DropHeals and boss Hearts.</summary>
public sealed partial class NpcSystem
{
    /// <summary>
    /// Rolls the kill's pickups: a boss defeat spills <see cref="Items.PickupConfig.BossHearts"/> Hearts; any other kill
    /// that a player struck, by a creature that does damage (never critters, parts or statue spawns), rolls one in
    /// <see cref="Items.PickupConfig.DropOneIn"/> for a Star (that player's mana not full) or else a Heart (life not
    /// full), and a magic last hit rolls an extra Star. Uses its own seeded stream so other rolls stay put.
    /// </summary>
    private void RollHeals(Npc npc, bool defeat)
    {
        if (npc.FromStatue || npc.IsPart || npc.MaxLife <= 1 || npc.Def.HasTag("critter")) return;
        var cfg = _world.Drops.Config;
        var rng = new Pcg32(Hash.Combine(Hash.Combine((ulong)npc.Id, _world.Session.Clock.Tick), 0x48454152UL));
        if (defeat && npc.Def.Boss != null && cfg.BossHearts is [var min, var max])
        {
            for (int i = rng.Range(min, Math.Max(min, max)); i > 0; i--) _world.SpawnHeart(npc.Center, Toss(ref rng));
            return;
        }
        if (npc.Def.Damage <= 0 || npc.LastAttacker is not { Alive: true } p) return;
        var v = p.State.Vitals;
        if (rng.NextInt(Math.Max(1, cfg.DropOneIn)) == 0)
        {
            if (rng.Chance(cfg.StarChance) && v.Mana < v.MaxMana) _world.SpawnManaStar(npc.Center, Toss(ref rng));
            else if (rng.Chance(cfg.HeartChance) && v.Life < v.MaxLife) _world.SpawnHeart(npc.Center, Toss(ref rng));
        }
        if (npc.LastHitMagic && v.Mana < v.MaxMana && rng.Chance(cfg.MagicStarChance)) _world.SpawnManaStar(npc.Center, Toss(ref rng));
    }

    private static Vector3 Toss(ref Pcg32 rng) => new(rng.NextSigned() * 1.5f, 3f, rng.NextSigned() * 1.5f);
}
