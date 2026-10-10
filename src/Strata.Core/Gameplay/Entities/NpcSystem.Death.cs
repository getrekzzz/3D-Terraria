using System.Numerics;
using Strata.Core.Content.Specs;
using Strata.Core.Determinism;
using Strata.Core.Gameplay.Items;

namespace Strata.Core.Gameplay.Entities;

public sealed partial class NpcSystem
{
    /// <summary>
    /// Another living boss shares this one's defeat flag (the other of a pair). Parts that die with their body (Moon
    /// Lord's hands) never hold the fight open: they fall with it.
    /// </summary>
    public bool GroupAlive(Content.Defs.BossInfo boss, Npc? except)
    {
        if (boss.Flag == null) return false;
        foreach (var n in _npcs)
            if (n != except && n.Alive && n.Def.Boss?.Flag == boss.Flag && !FallsWithBody(n)) return true;
        return false;
    }

    private static bool FallsWithBody(Npc n) => n.Leader != null && n.Part is { DiesWithBody: true };

    /// <summary>
    /// Kills a creature: its drops and coins, death effects, and the boss defeat when its group has fallen. A part whose
    /// body already fell is only removed (no loot, effects or second defeat), and a body's falling parts die with it.
    /// </summary>
    public void Kill(Npc npc)
    {
        if (FallsWithBody(npc) && npc.Leader is { } body && (!body.Alive || body.Removed))
        {
            npc.Life = 0;
            Despawn(npc);
            return;
        }
        npc.Life = 0;
        foreach (var f in npc.Followers)
            if (f.IsPart && f.Part is { DiesWithBody: true }) f.Life = 0; // FollowLeader removes them next tick
        foreach (var n in _npcs)
            if (n.BoundTo == npc) n.Life = 0; // bound children (Golem's freed head) fall with it, removed the same way
        if (npc.Def.Boss is { SegmentLife.Length: > 0, SegmentLoot: not null }) RollSegmentLoot(npc, npc.Center); // the head is a segment too
        var cell = npc.Cell;
        bool defeat = npc.Def.Boss is { Minor: false } group && !GroupAlive(group, npc);
        bool rolls = npc.Def.Boss is not { LootOnDefeat: true } || defeat;
        var drops = new List<ItemStack>();
        if (npc.Def.Loot != null && rolls && !npc.FromStatue)
        {
            var rng = new Pcg32(Hash.Combine((ulong)npc.Id, _world.Session.Clock.Tick));
            drops.AddRange(_loot.Roll(npc.Def.Loot, ref rng, _world.Session.ContextAt(cell)));
        }
        // Coins come from the loot table when it names them, else from the creature's value (never both).
        bool tableCoins = npc.Def.Loot != null && _world.Session.Content.LootTables.TryGet(npc.Def.Loot, out var table) && table.Coins != null;
        if (npc.Def.Value > 0 && !tableCoins && rolls && !npc.FromStatue) drops.AddRange(Coins.ToStacks(_world.Session.Content, (long)(npc.Def.Value * _rng.Range(0.75f, 1.25f))));
        if (rolls && !npc.FromStatue)
        {
            RollGlobalDrops(npc, cell, drops);
            RollGlobalKill(npc, drops);
        }
        foreach (var stack in drops)
            _world.Drops.Spawn(stack, npc.Center, new Vector3(_rng.NextSigned() * 1.5f, 3f, _rng.NextSigned() * 1.5f));
        if (rolls) RollHeals(npc, defeat);
        DeathEffects(npc);
        CountShieldKill(npc);
        if (npc.Def.Boss is { Minor: true, Flag: { } minorFlag }) _world.Progression.Set(minorFlag);
        if (defeat && npc.Def.Boss is { } boss)
        {
            if (boss.Flag != null) _world.Progression.Set(boss.Flag);
            foreach (var extra in boss.AlsoFlags) _world.Progression.Set(extra);
            BossDefeated?.Invoke(npc);
            Messaged?.Invoke($"{boss.Title} has been defeated!", Messages.MessageKind.BossDefeated);
        }
        Killed?.Invoke(npc);
        int index = _npcs.IndexOf(npc);
        if (index >= 0) Remove(index);
    }

    private void DeathEffects(Npc npc)
    {
        var content = _world.Session.Content;
        foreach (var fx in npc.Def.OnDeath)
        {
            switch (fx.Type)
            {
                case CreatureEffectSpecs.PourLiquid:
                    var pour = fx.Parse<PourLiquidParams>();
                    if (content.Liquids.TryGet(pour.Liquid, out var liquid)) _world.Editor.PourLiquid(npc.Cell, liquid, pour.Amount);
                    break;
                case CreatureEffectSpecs.SpawnEnemies:
                    var spawn = fx.Parse<SpawnEnemiesParams>();
                    if (!content.Enemies.TryGet(spawn.Enemy, out var child)) break;
                    if (spawn.Tether)
                    {
                        // Bound to the body: rises where the part fell, without the scatter or the toss.
                        var body = npc.Leader is { Alive: true, Removed: false } l ? l : null;
                        for (int i = 0; i < spawn.Count; i++)
                            if (Spawn(child, npc.Position) is { } bound) bound.BoundTo = body;
                        break;
                    }
                    for (int i = 0; i < spawn.Count; i++)
                        if (Spawn(child, npc.Position + new Vector3(_rng.NextSigned() * 0.6f, 0.2f, _rng.NextSigned() * 0.6f)) is { } c)
                            c.Velocity = new Vector3(_rng.NextSigned() * 3f, 4f, _rng.NextSigned() * 3f);
                    break;
            }
        }
    }

    /// <summary>Table rolled after a creature's own loot for every kill that counts: the Dungeon's Golden Keys.</summary>
    public const string GlobalKillTable = Content.Index.ItemSourceIndex.GlobalKillTable;

    /// <summary>
    /// Rolls <see cref="GlobalKillTable"/> (when it exists) for kills worth coins, never critters or parts; its
    /// conditions see the spot the creature died at (zones such as the dungeon included).
    /// </summary>
    private void RollGlobalKill(Npc npc, List<ItemStack> into)
    {
        if (npc.Def.Value <= 0 || npc.IsPart || npc.Def.HasTag("critter") || !_world.Session.Content.LootTables.Contains(GlobalKillTable)) return;
        var rng = new Pcg32(Hash.Combine(Hash.Combine((ulong)npc.Id, _world.Session.Clock.Tick), 0x474B4559UL));
        into.AddRange(_loot.Roll(GlobalKillTable, ref rng, _world.Session.ContextAt(npc.Cell)));
    }
}
