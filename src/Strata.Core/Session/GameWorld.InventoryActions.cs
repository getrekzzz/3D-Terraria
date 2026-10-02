using System.Numerics;
using Strata.Core.Common;
using Strata.Core.Gameplay.Items;
using Strata.Core.Gameplay.Town;
using Strata.Core.Voxels.TileObjects;

namespace Strata.Core.Session;

/// <summary>
/// What is open beside the inventory when a slot is clicked: a chest (its storage, and the placed object whose chunk
/// saves it) or a resident's shop.
/// </summary>
public readonly record struct SlotContext(Inventory? Container = null, TownNpc? Shop = null, TileObjectInstance? Chest = null)
{
    public static SlotContext None => default;

    /// <summary>A placed chest (or other container object) open beside the inventory.</summary>
    public static SlotContext Of(TileObjectInstance chest) =>
        chest.Container == null ? default : new SlotContext(new Inventory(chest.Container), null, chest);
}

public sealed partial class GameWorld
{
    /// <summary>
    /// One click on an inventory slot, routed as the reference game does. With an empty cursor a left click with Alt
    /// toggles the favourite mark (backpack, coin and ammo slots), with Ctrl (no shop open) moves the stack to the trash
    /// slot (replacing what was there; favourites refuse) and with Shift quick-moves it: chest and backpack both ways,
    /// equip from the backpack, unequip to the backpack, or sell while a shop is open. A right click opens a crate,
    /// bag or present in the backpack; every other click is <see cref="SlotActions"/> with the slot's
    /// <see cref="SlotFilters"/>. A chest that changed is marked for saving.
    /// </summary>
    public ClickOutcome ClickSlot(PlayerEntity p, SlotRef slot, bool right, ClickMods mods, SlotContext ctx)
    {
        var inv = p.State.Inventory;
        var storage = slot.Area == SlotArea.Container ? ctx.Container : inv.Resolve(slot.Area);
        if (storage == null || (uint)slot.Index >= (uint)storage.Size) return ClickOutcome.None;
        var outcome = right ? RightClickSlot(p, slot, storage) : LeftClickSlot(p, slot, storage, mods, ctx);
        if (outcome != ClickOutcome.None && ctx.Chest != null) MarkObjectModified(ctx.Chest);
        return outcome;
    }

    /// <summary>Throws the cursor stack (or one of it) where the player looks; returns <see cref="ClickOutcome.Thrown"/> if anything flew.</summary>
    public ClickOutcome ThrowCursor(PlayerEntity p, bool one)
    {
        var inv = p.State.Inventory;
        var cursor = inv.Cursor;
        if (cursor.IsEmpty) return ClickOutcome.None;
        var thrown = one ? cursor.WithCount(1) : cursor;
        inv.Cursor = cursor.WithCount(cursor.Count - thrown.Count);
        ThrowStack(p, thrown);
        return ClickOutcome.Thrown;
    }

    /// <summary>
    /// Tosses a stack out ahead of the player, as the drop key does: from just in front of the eyes, forwards and a
    /// little up, and not picked up again for 1.5 s.
    /// </summary>
    public DroppedItem? ThrowStack(PlayerEntity p, ItemStack s)
    {
        if (s.IsEmpty) return null;
        var aim = p.Aim;
        return Drops.Spawn(s, p.Eye + aim * 0.6f, new Vector3(aim.X, 0.4f, aim.Z) * 5f, pickupDelay: 90);
    }

    private ClickOutcome RightClickSlot(PlayerEntity p, SlotRef slot, Inventory storage)
    {
        var s = storage[slot.Index];
        if (slot.Area == SlotArea.Backpack && !s.IsEmpty && ItemUse.CanOpen(s.Item!))
            return ItemUse.OpenFromSlot(p, storage, slot.Index) ? ClickOutcome.Opened : ClickOutcome.None;
        return SlotActions.RightClick(p.State.Inventory, slot, storage);
    }

    private ClickOutcome LeftClickSlot(PlayerEntity p, SlotRef slot, Inventory storage, ClickMods mods, SlotContext ctx)
    {
        var inv = p.State.Inventory;
        var cursor = inv.Cursor;
        // Modified clicks act on the slot's stack and need a free cursor; with something held they are plain clicks.
        if (cursor.IsEmpty && !storage[slot.Index].IsEmpty)
        {
            if ((mods & ClickMods.Alt) != 0) return ToggleFavorite(storage, slot.Index);
            if ((mods & ClickMods.Ctrl) != 0 && ctx.Shop == null) return Trash(inv, slot, storage);
            if ((mods & ClickMods.Shift) != 0) return QuickMove(p, slot, storage, ctx);
        }
        SlotActions.LeftClick(ref cursor, storage, slot.Index, SlotFilters.For(slot, inv));
        if (cursor.Equals(inv.Cursor)) return ClickOutcome.None;
        inv.Cursor = cursor;
        return ClickOutcome.Moved;
    }

    private static ClickOutcome ToggleFavorite(Inventory storage, int index)
    {
        if (!storage.KeepsFavorites) return ClickOutcome.None;
        storage.SetFavorite(index, !storage.IsFavorite(index));
        return ClickOutcome.Favorited;
    }

    private static ClickOutcome Trash(PlayerInventory inv, SlotRef slot, Inventory storage)
    {
        if (slot.Area is not (SlotArea.Backpack or SlotArea.Coins or SlotArea.Ammo or SlotArea.Container) || storage.IsFavorite(slot.Index))
            return ClickOutcome.None;
        inv.Trash[0] = storage[slot.Index];
        storage[slot.Index] = ItemStack.Empty;
        return ClickOutcome.Trashed;
    }

    private ClickOutcome QuickMove(PlayerEntity p, SlotRef slot, Inventory storage, SlotContext ctx)
    {
        var inv = p.State.Inventory;
        int i = slot.Index;
        var s = storage[i];
        switch (slot.Area)
        {
            case SlotArea.Backpack or SlotArea.Coins or SlotArea.Ammo when ctx.Shop != null:
                if (storage.IsFavorite(i)) return ClickOutcome.None;
                return Commerce.Sell(p, storage, i) == TradeResult.Ok ? ClickOutcome.Sold : ClickOutcome.None;
            case SlotArea.Backpack or SlotArea.Coins or SlotArea.Ammo when ctx.Container != null:
                storage[i] = ctx.Container.Add(s);
                break;
            case SlotArea.Backpack:
                return SlotActions.IsWearable(s.Item!) && inv.EquipFrom(storage, i) ? ClickOutcome.Equipped : ClickOutcome.None;
            case SlotArea.Coins or SlotArea.Ammo or SlotArea.Shop:
                return ClickOutcome.None;
            default:
                // Out of a chest, the equipment, the trash or the service slot: into the backpack (coins and ammo to their slots).
                storage[i] = inv.Pickup(s);
                break;
        }
        return storage[i].Equals(s) ? ClickOutcome.None : ClickOutcome.Moved;
    }

    private void MarkObjectModified(TileObjectInstance obj) =>
        Session.World.GetChunk(ChunkMath.CellToChunk(obj.Anchor))?.MarkModified();
}
