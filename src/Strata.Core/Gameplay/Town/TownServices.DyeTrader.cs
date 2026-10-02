using Strata.Core.Content.Specs;
using Strata.Core.Gameplay.Items;
using Strata.Core.Gameplay.Loot;
using Strata.Core.Session;

namespace Strata.Core.Gameplay.Town;

public sealed partial class TownServices
{
    // ---- The Dye Trader's trade --------------------------------------------------------------------------------

    private LootRoller? _loot;

    /// <summary>Whether <paramref name="npc"/> trades and the player carries something they take (shows the 'Trade' button).</summary>
    public bool CanTradeStrangePlant(TownNpc npc, PlayerEntity player) =>
        npc.Def.FindService(TownServiceSpecs.DyeTrade) is { } spec && player.State.Inventory.Backpack.Count(d => d.HasTag(spec.Parse<DyeTradeParams>().AcceptsTag)) > 0;

    /// <summary>Takes one Strange Plant from the backpack and gives a roll of the trade table (a rare dye) for it.</summary>
    public TradeResult TradeStrangePlant(TownNpc npc, PlayerEntity player, out ItemStack dye)
    {
        dye = ItemStack.Empty;
        if (npc.Def.FindService(TownServiceSpecs.DyeTrade) is not { } spec) return TradeResult.Unavailable;
        var p = spec.Parse<DyeTradeParams>();
        var inv = player.State.Inventory;
        if (inv.Backpack.Count(d => d.HasTag(p.AcceptsTag)) < 1) return TradeResult.NothingToDo;
        _loot ??= new LootRoller(Content);
        var rolled = _loot.Roll(p.Table, ref _rng);
        if (rolled.Count == 0) return TradeResult.Unavailable;
        inv.Backpack.Remove(d => d.HasTag(p.AcceptsTag), 1);
        dye = rolled[0];
        foreach (var s in rolled)
        {
            var rest = inv.Pickup(s);
            if (!rest.IsEmpty) _world.Drops.Spawn(rest, player.Center, System.Numerics.Vector3.Zero);
        }
        return TradeResult.Ok;
    }
}
