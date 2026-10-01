using Strata.Core.Determinism;
using Strata.Core.Gameplay.Items;

namespace Strata.Core.Gameplay.Entities;

public sealed partial class NpcSystem
{
    /// <summary>
    /// Loot table every hostile creature also rolls when slain (seasonal goodie bags and presents; its entries carry
    /// their own conditions). Critters, statue creatures and town residents never roll it.
    /// </summary>
    public const string GlobalDropTable = "enemy_global_drops";

    private void RollGlobalDrops(Npc npc, Common.Int3 cell, List<ItemStack> drops)
    {
        if (npc.FromStatue || npc.Def.HasTag("critter") || !_world.Session.Content.LootTables.Contains(GlobalDropTable)) return;
        var rng = new Pcg32(Hash.Combine((ulong)npc.Id, _world.Session.Clock.Tick ^ 0x5EA5_0DL));
        drops.AddRange(_loot.Roll(GlobalDropTable, ref rng, _world.Session.ContextAt(cell)));
    }
}
