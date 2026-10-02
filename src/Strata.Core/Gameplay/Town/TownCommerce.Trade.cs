using Strata.Core.Content.Defs;
using Strata.Core.Gameplay.Items;
using Strata.Core.Session;

namespace Strata.Core.Gameplay.Town;

/// <summary>Where a purchase goes: into the player's storage (commands, Shift-click) or onto the inventory cursor (a shop click).</summary>
public enum BuyTarget
{
    Backpack,
    Cursor,
}

/// <summary>
/// Trading as Terraria's shop does. Buying puts the goods in the backpack or on the cursor, where a click adds to the
/// same item already held and refuses when something else is held or the stack is full. Selling takes a stack from a
/// slot or the cursor for its sale value: favourites and coins never sell, and an item of no value goes for nothing,
/// as dropping it on a shop does in Terraria. Each player's last <see cref="BuybackCapacity"/> sold stacks wait,
/// newest first, to be bought back at the price they fetched. The buyback list is not saved.
/// </summary>
public sealed partial class TownCommerce
{
    /// <summary>Sold stacks kept for buying back, per player.</summary>
    public const int BuybackCapacity = 40;

    private readonly Dictionary<PlayerEntity, List<ItemStack>> _buyback = new();

    /// <summary>Buys <paramref name="count"/> of <paramref name="item"/> from the resident's current stock.</summary>
    public TradeResult Buy(TownNpc npc, PlayerEntity player, ItemDef item, int count = 1, BuyTarget target = BuyTarget.Backpack)
    {
        if (count <= 0) return TradeResult.NothingToDo;
        var offer = Offer(npc, player, item);
        if (offer == null) return TradeResult.Unavailable;
        return Deliver(player, new ItemStack(item, count), offer.Price * count, target, rollPrefixes: true, offer.Currency);
    }

    /// <summary>Sale value of a stack (copper).</summary>
    public long SaleValue(ItemStack stack) => Prefixes.SellValue(stack, Config.SellRatio);

    /// <summary>Sells the whole stack in a backpack slot.</summary>
    public TradeResult Sell(PlayerEntity player, int slot) => Sell(player, player.State.Inventory.Backpack, slot);

    /// <summary>Sells the whole stack in a slot of one of the player's storages (backpack or ammo; favourites and coins never).</summary>
    public TradeResult Sell(PlayerEntity player, Inventory from, int slot)
    {
        var stack = from[slot];
        if (stack.IsEmpty || Coins.IsCoin(stack) || from.IsFavorite(slot)) return TradeResult.NotSold;
        from.Take(slot, stack.Count);
        Sold(player, stack);
        return TradeResult.Ok;
    }

    /// <summary>Sells the stack held on the inventory cursor (dropped on the shop).</summary>
    public TradeResult SellCursor(PlayerEntity player)
    {
        var inv = player.State.Inventory;
        var stack = inv.Cursor;
        if (stack.IsEmpty || Coins.IsCoin(stack)) return TradeResult.NotSold;
        inv.Cursor = ItemStack.Empty;
        Sold(player, stack);
        return TradeResult.Ok;
    }

    /// <summary>The player's sold stacks that can be bought back, newest first.</summary>
    public IReadOnlyList<ItemStack> Buyback(PlayerEntity player) =>
        _buyback.TryGetValue(player, out var list) ? list : Array.Empty<ItemStack>();

    /// <summary>Buys back the sold stack at <paramref name="index"/> of <see cref="Buyback"/> for what it fetched.</summary>
    public TradeResult BuyBack(PlayerEntity player, int index, BuyTarget target = BuyTarget.Cursor)
    {
        if (!_buyback.TryGetValue(player, out var list) || (uint)index >= (uint)list.Count) return TradeResult.Unavailable;
        var stack = list[index];
        var result = Deliver(player, stack, SaleValue(stack), target);
        if (result == TradeResult.Ok) list.RemoveAt(index);
        return result;
    }

    /// <summary>Pays for the sold stack and keeps it for buyback (the oldest falls off past the capacity).</summary>
    private void Sold(PlayerEntity player, ItemStack stack)
    {
        Drop(player, Wallet.Give(player.State.Inventory, Content, SaleValue(stack)));
        if (!_buyback.TryGetValue(player, out var list)) _buyback[player] = list = new List<ItemStack>(BuybackCapacity + 1);
        list.Insert(0, stack);
        if (list.Count > BuybackCapacity) list.RemoveAt(list.Count - 1);
    }

    /// <summary>
    /// Takes <paramref name="price"/> (purse first, then the Piggy Bank and Safe) and hands over <paramref name="goods"/>,
    /// or changes nothing. New goods (<paramref name="rollPrefixes"/>) come with a prefix as crafted ones do, each
    /// weapon or accessory rolled separately; a stack bought back keeps its own.
    /// </summary>
    /// <param name="currency">Paid in this item from the backpack instead of coins (Defender Medals and the like).</param>
    private TradeResult Deliver(PlayerEntity player, ItemStack goods, long price, BuyTarget target, bool rollPrefixes = false, ItemDef? currency = null)
    {
        var inv = player.State.Inventory;
        var cursor = inv.Cursor;
        bool fits = target == BuyTarget.Cursor
            ? (cursor.IsEmpty && goods.Count <= goods.Item!.MaxStack) || (cursor.CanStackWith(goods) && cursor.SpaceLeft >= goods.Count)
            : inv.CanTake(goods);
        if (!fits) return TradeResult.NoRoom;
        if (currency != null)
        {
            if (inv.Backpack.Count(currency) < price) return TradeResult.TooPoor;
            inv.Backpack.Remove(d => d == currency, (int)price);
        }
        else
        {
            if (!Wallet.TrySpend(inv, player.State.BanksForPaying(), Content, price, out var change)) return TradeResult.TooPoor;
            Drop(player, change);
        }
        var units = rollPrefixes ? Prefixes.Made(goods.Item!, goods.Count, Content, ref _rng) : new List<ItemStack> { goods };
        if (target == BuyTarget.Cursor) inv.Cursor = cursor.IsEmpty && units.Count == 1 ? units[0] : cursor.IsEmpty ? goods : cursor.WithCount(cursor.Count + goods.Count);
        else
            foreach (var unit in units)
                if (inv.Pickup(unit) is { IsEmpty: false } rest) _world.Drops.Spawn(rest, player.Center, System.Numerics.Vector3.Zero);
        return TradeResult.Ok;
    }

    /// <summary>Coins that fit nowhere fall at the player's feet.</summary>
    private void Drop(PlayerEntity player, List<ItemStack> stacks)
    {
        foreach (var s in stacks)
            if (!s.IsEmpty) _world.Drops.Spawn(s, player.Center, System.Numerics.Vector3.Zero);
    }
}
