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

    /// <summary>
    /// Prefix of a creature's extra loot table ("extra_king_slime"): drops added by other content (mounts) on top of the
    /// creature's own table, so they need not edit it.
    /// </summary>
    public const string ExtraDropPrefix = "extra_";

    private void RollGlobalDrops(Npc npc, Common.Int3 cell, List<ItemStack> drops)
    {
        if (npc.FromStatue || npc.Def.HasTag("critter")) return;
        var tables = _world.Session.Content.LootTables;
        bool global = tables.Contains(GlobalDropTable);
        string extra = ExtraDropPrefix + npc.Def.Id;
        bool own = tables.Contains(extra);
        if (!global && !own) return;
        var rng = new Pcg32(Hash.Combine((ulong)npc.Id, _world.Session.Clock.Tick ^ 0x5EA5_0DL));
        var ctx = _world.Session.ContextAt(cell);
        if (global) drops.AddRange(_loot.Roll(GlobalDropTable, ref rng, ctx));
        if (own) drops.AddRange(_loot.Roll(extra, ref rng, ctx));
    }
}
