using Strata.Core.Content.Defs;

namespace Strata.Core.Gameplay.Items;

/// <summary>
/// The reference game's chest and inventory buttons: Loot All, Deposit All, Quick Stack, Restock and Sort. Favourited
/// slots never move, the hotbar stays out of deposits and quick stacks, and coins belong to the purse. The counting
/// operations return how many items moved.
/// </summary>
public static class InventoryOps
{
    /// <summary>Takes everything out of a chest as a pickup would (coins to the coin slots, ammo to the ammo slots); what does not fit stays.</summary>
    public static int LootAll(Inventory chest, PlayerInventory inv)
    {
        int moved = 0;
        for (int i = 0; i < chest.Size; i++)
        {
            var s = chest[i];
            if (s.IsEmpty) continue;
            var rest = inv.Pickup(s);
            if (rest.Count == s.Count) continue;
            chest[i] = rest;
            moved += s.Count - rest.Count;
        }
        return moved;
    }

    /// <summary>Puts every backpack stack below the hotbar into the chest, topping up its stacks first; favourites and coins stay.</summary>
    public static int DepositAll(PlayerInventory inv, Inventory chest) => MoveBackpack(inv, chest, onlyKnown: false);

    /// <summary>Like <see cref="DepositAll"/>, but only items the chest already holds (they fill its stacks, then free slots).</summary>
    public static int QuickStack(PlayerInventory inv, Inventory chest) => MoveBackpack(inv, chest, onlyKnown: true);

    /// <summary>Tops up every partial stack in the backpack and the ammo slots from matching stacks in the chest.</summary>
    public static int Restock(Inventory chest, PlayerInventory inv) => TopUp(chest, inv.Backpack) + TopUp(chest, inv.Ammo);

    /// <summary>Sorts a whole chest.</summary>
    public static void SortChest(Inventory chest) => Sort(chest, 0, chest.Size);

    /// <summary>
    /// Sorts the slots <paramref name="from"/> (inclusive) to <paramref name="to"/> (exclusive): partial stacks of the
    /// same item and prefix merge, then stacks line up by group (weapons and ammo, tools, armor and accessories,
    /// potions, placeables, materials, the rest), rarer first, then by id. Favourited slots keep their stacks and place.
    /// </summary>
    public static void Sort(Inventory inv, int from, int to)
    {
        from = Math.Max(0, from);
        to = Math.Min(inv.Size, to);
        var merged = new List<ItemStack>();
        for (int i = from; i < to; i++)
        {
            var s = inv[i];
            if (s.IsEmpty || inv.IsFavorite(i)) continue;
            int left = s.Count;
            for (int j = 0; j < merged.Count && left > 0; j++)
            {
                if (!merged[j].CanStackWith(s) || merged[j].SpaceLeft <= 0) continue;
                int n = Math.Min(left, merged[j].SpaceLeft);
                merged[j] = merged[j].WithCount(merged[j].Count + n);
                left -= n;
            }
            if (left > 0) merged.Add(s.WithCount(left));
        }
        merged.Sort(Compare);
        int next = 0;
        for (int i = from; i < to; i++)
        {
            if (inv.IsFavorite(i)) continue;
            var s = next < merged.Count ? merged[next++] : ItemStack.Empty;
            if (!inv[i].Equals(s)) inv[i] = s;
        }
    }

    /// <summary>Sort group of an item: 0 weapons and ammo, 1 tools, 2 armor and accessories, 3 potions, 4 placeables, 5 materials, 6 the rest.</summary>
    public static int Group(ItemDef item) => item.Category switch
    {
        ItemCategory.Weapon or ItemCategory.Ammo => 0,
        ItemCategory.Tool or ItemCategory.Fishing or ItemCategory.Wiring or ItemCategory.Hook => 1,
        ItemCategory.Armor or ItemCategory.Accessory or ItemCategory.Vanity or ItemCategory.Dye
            or ItemCategory.Mount or ItemCategory.Minecart or ItemCategory.Pet => 2,
        ItemCategory.Potion or ItemCategory.Consumable => 3,
        ItemCategory.Block or ItemCategory.Placeable or ItemCategory.Furniture or ItemCategory.Station or ItemCategory.Light => 4,
        ItemCategory.Material => 5,
        _ => 6,
    };

    private static int Compare(ItemStack a, ItemStack b)
    {
        int c = Group(a.Item!).CompareTo(Group(b.Item!));
        if (c == 0) c = b.Item!.Rarity.CompareTo(a.Item!.Rarity);
        if (c == 0) c = string.CompareOrdinal(a.Item!.Id, b.Item!.Id);
        if (c == 0) c = string.CompareOrdinal(a.Prefix?.Id, b.Prefix?.Id);
        return c != 0 ? c : b.Count.CompareTo(a.Count);
    }

    private static int MoveBackpack(PlayerInventory inv, Inventory chest, bool onlyKnown)
    {
        var backpack = inv.Backpack;
        int moved = 0;
        for (int i = PlayerInventory.HotbarSize; i < backpack.Size; i++)
        {
            var s = backpack[i];
            if (s.IsEmpty || backpack.IsFavorite(i) || Coins.IsCoin(s) || (onlyKnown && chest.Count(s.Item!) == 0)) continue;
            var rest = chest.Add(s);
            if (rest.Count == s.Count) continue;
            backpack[i] = rest;
            moved += s.Count - rest.Count;
        }
        return moved;
    }

    private static int TopUp(Inventory chest, Inventory target)
    {
        int moved = 0;
        for (int t = 0; t < target.Size; t++)
        {
            var want = target[t];
            for (int c = 0; c < chest.Size && want.SpaceLeft > 0; c++)
            {
                var have = chest[c];
                if (!want.CanStackWith(have)) continue;
                int n = Math.Min(want.SpaceLeft, have.Count);
                chest[c] = have.WithCount(have.Count - n);
                want = want.WithCount(want.Count + n);
                moved += n;
            }
            if (!want.Equals(target[t])) target[t] = want;
        }
        return moved;
    }
}
