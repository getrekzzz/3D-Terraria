using System.Numerics;
using Strata.Core.Content.Specs;
using Strata.Core.Determinism;
using Strata.Core.Gameplay.Items;

namespace Strata.Core.Gameplay.Entities;

public sealed partial class NpcSystem
{
    /// <summary>Another living boss shares this one's defeat flag (the other of a pair).</summary>
    public bool GroupAlive(Content.Defs.BossInfo boss, Npc? except) =>
        boss.Flag != null && _npcs.Any(n => n != except && n.Alive && n.Def.Boss?.Flag == boss.Flag);

    public void Kill(Npc npc)
    {
        npc.Life = 0;
        var cell = npc.Cell;
        var drops = new List<ItemStack>();
        if (npc.Def.Loot != null)
        {
            var rng = new Pcg32(Hash.Combine((ulong)npc.Id, _world.Session.Clock.Tick));
            drops.AddRange(_loot.Roll(npc.Def.Loot, ref rng, _world.Session.ContextAt(cell)));
        }
        // Coins come from the loot table when it names them, else from the creature's value (never both).
        bool tableCoins = npc.Def.Loot != null && _world.Session.Content.LootTables.TryGet(npc.Def.Loot, out var table) && table.Coins != null;
        if (npc.Def.Value > 0 && !tableCoins) drops.AddRange(Coins.ToStacks(_world.Session.Content, (long)(npc.Def.Value * _rng.Range(0.75f, 1.25f))));
        foreach (var stack in drops)
            _world.Drops.Spawn(stack, npc.Center, new Vector3(_rng.NextSigned() * 1.5f, 3f, _rng.NextSigned() * 1.5f));
        DeathEffects(npc);
        CountShieldKill(npc);
        if (npc.Def.Boss is { Minor: true, Flag: { } minorFlag }) _world.Progression.Set(minorFlag);
        if (npc.Def.Boss is { Minor: false } boss && !GroupAlive(boss, npc))
        {
            if (boss.Flag != null) _world.Progression.Set(boss.Flag);
            foreach (var extra in boss.AlsoFlags) _world.Progression.Set(extra);
            BossDefeated?.Invoke(npc);
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
                    for (int i = 0; i < spawn.Count; i++)
                        if (Spawn(child, npc.Position + new Vector3(_rng.NextSigned() * 0.6f, 0.2f, _rng.NextSigned() * 0.6f)) is { } c)
                            c.Velocity = new Vector3(_rng.NextSigned() * 3f, 4f, _rng.NextSigned() * 3f);
                    break;
            }
        }
    }
}
