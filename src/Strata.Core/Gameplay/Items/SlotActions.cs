using Strata.Core.Content.Defs;

namespace Strata.Core.Gameplay.Items;

/// <summary>
/// Mouse interaction rules for inventory slots with a cursor ("held by the mouse") stack, matching the reference
/// game: left click picks up / places / merges / swaps, right click takes half or drops one, shift-click moves
/// between inventories. Slot filters restrict equipment slots.
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

    public static void RightClick(ref ItemStack cursor, Inventory inv, int slot, Func<ItemDef, bool>? accepts = null)
    {
        var s = inv[slot];
        if (cursor.IsEmpty)
        {
            if (s.IsEmpty) return;
            int half = (s.Count + 1) / 2;
            cursor = s.WithCount(half);
            inv[slot] = s.WithCount(s.Count - half);
            return;
        }
        if (accepts != null && !accepts(cursor.Item!)) return;
        if (s.IsEmpty)
        {
            inv[slot] = cursor.WithCount(1);
            cursor = cursor.WithCount(cursor.Count - 1);
        }
        else if (s.CanStackWith(cursor) && s.SpaceLeft > 0)
        {
            inv[slot] = s.WithCount(s.Count + 1);
            cursor = cursor.WithCount(cursor.Count - 1);
        }
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

    public static Func<ItemDef, bool> ArmorFilter(EquipSlot slot) => item => item.EquipSlot == slot;

    /// <summary>Accessories only, and never two of the same accessory.</summary>
    public static Func<ItemDef, bool> AccessoryFilter(Inventory accessories, int slot) =>
        item => item.EquipSlot == EquipSlot.Accessory && !Enumerable.Range(0, accessories.Size).Any(i => i != slot && accessories[i].Item == item);
}
