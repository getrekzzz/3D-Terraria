using Strata.Core.Content.Defs;

namespace Strata.Core.Gameplay.Items;

/// <summary>
/// Mouse interaction rules for inventory slots with a cursor ("held by the mouse") stack, matching the reference
/// game: left click picks up / places / merges / swaps, right click equips, swaps with vanity or takes one, and
/// <see cref="QuickMove"/> moves between inventories. <see cref="SlotFilters"/> restrict which slots take an item;
/// modifier keys (trash, favourite, quick move, sell) are routed by <c>GameWorld.ClickSlot</c>.
/// </summary>
public static class SlotActions
{
    public static void LeftClick(ref ItemStack cursor, Inventory inv, int slot, Func<ItemDef, bool>? accepts = null)
    {
        var s = inv[slot];
        if (cursor.IsEmpty)
        {
            if (s.IsEmpty) return;
            cursor = s;
            inv[slot] = ItemStack.Empty;
            return;
        }
        if (accepts != null && !accepts(cursor.Item!)) return;
        if (s.IsEmpty)
        {
            inv[slot] = cursor;
            cursor = ItemStack.Empty;
            return;
        }
        if (s.CanStackWith(cursor))
        {
            int moved = Math.Min(cursor.Count, s.SpaceLeft);
            inv[slot] = s.WithCount(s.Count + moved);
            cursor = cursor.WithCount(cursor.Count - moved);
            return;
        }
        inv[slot] = cursor;
        cursor = s;
    }

    /// <summary>
    /// The reference game's right click on a slot (<paramref name="inv"/> is the storage behind <paramref name="slot"/>):
    /// wearable items in the backpack or a chest are equipped, swapping out what was worn (<see cref="PlayerInventory.EquipFrom"/>);
    /// in the armor and accessory rows the item swaps with the vanity slot of the same index (both ways, when each
    /// side fits the other row); anything else puts ONE item on the cursor, when the cursor is empty or holds the same
    /// item with room left. Held down, the screen repeats it at <see cref="HoldRepeat.IntervalTicks"/>.
    /// </summary>
    public static ClickOutcome RightClick(PlayerInventory player, SlotRef slot, Inventory inv)
    {
        if (slot.Area is SlotArea.Armor or SlotArea.Accessories or SlotArea.VanityArmor or SlotArea.VanityAccessories)
            return SwapVanity(player, slot) ? ClickOutcome.Equipped : ClickOutcome.None;
        var s = inv[slot.Index];
        if (s.IsEmpty) return ClickOutcome.None;
        if ((slot.Area is SlotArea.Backpack or SlotArea.Container) && IsWearable(s.Item!))
            return player.EquipFrom(inv, slot.Index) ? ClickOutcome.Equipped : ClickOutcome.None;
        var cursor = player.Cursor;
        if (!cursor.IsEmpty && !(cursor.CanStackWith(s) && cursor.SpaceLeft > 0)) return ClickOutcome.None;
        player.Cursor = cursor.IsEmpty ? s.WithCount(1) : cursor.WithCount(cursor.Count + 1);
        inv[slot.Index] = s.WithCount(s.Count - 1);
        return ClickOutcome.Moved;
    }

    /// <summary>Armor, accessories, vanity, dyes and the misc row (what <see cref="PlayerInventory.EquipFrom"/> can wear).</summary>
    public static bool IsWearable(ItemDef item) => item.EquipSlot != EquipSlot.None || item.Category == ItemCategory.Dye;

    private static bool SwapVanity(PlayerInventory p, SlotRef slot)
    {
        bool armor = slot.Area is SlotArea.Armor or SlotArea.VanityArmor;
        var worn = armor ? p.Armor : p.Accessories;
        var looks = armor ? p.VanityArmor : p.VanityAccessories;
        int i = slot.Index;
        ItemStack toWorn = looks[i], toLooks = worn[i];
        if (toWorn.IsEmpty && toLooks.IsEmpty) return false;
        var fitsWorn = SlotFilters.For(new SlotRef(armor ? SlotArea.Armor : SlotArea.Accessories, i), p)!;
        var fitsLooks = SlotFilters.For(new SlotRef(armor ? SlotArea.VanityArmor : SlotArea.VanityAccessories, i), p)!;
        if ((!toWorn.IsEmpty && !fitsWorn(toWorn.Item!)) || (!toLooks.IsEmpty && !fitsLooks(toLooks.Item!))) return false;
        worn[i] = toWorn;
        looks[i] = toLooks;
        return true;
    }

    /// <summary>Moves a slot's stack into another inventory (merging first); what does not fit stays.</summary>
    public static void QuickMove(Inventory from, int slot, Inventory to, Func<ItemDef, bool>? accepts = null)
    {
        var s = from[slot];
        if (s.IsEmpty || (accepts != null && !accepts(s.Item!))) return;
        from[slot] = to.Add(s);
    }

    /// <summary>Returns the cursor stack to an inventory (closing the UI); returns what did not fit.</summary>
    public static ItemStack Stow(ref ItemStack cursor, Inventory inv)
    {
        var rest = inv.Add(cursor);
        cursor = ItemStack.Empty;
        return rest;
    }

}
