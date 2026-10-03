using System.Numerics;
using Strata.Core.Common;
using Strata.Core.Gameplay.Items;
using Strata.Core.Gameplay.Town;
using Strata.Core.Voxels.TileObjects;

namespace Strata.Core.Session;

/// <summary>
/// What is open beside the inventory when a slot is clicked: a chest (its storage, and the placed object whose chunk
/// saves it), a resident's shop, or a resident's service slot (<see cref="Service"/>: the Goblin Tinkerer's reforge).
/// </summary>
public readonly record struct SlotContext(Inventory? Container = null, TownNpc? Shop = null, TileObjectInstance? Chest = null, bool Service = false)
{
    public static SlotContext None => default;

    /// <summary>
    /// A placed chest (or other container object) open beside the inventory; a Piggy Bank or Safe shows the
    /// <paramref name="owner"/>'s personal storage of that kind.
    /// </summary>
    public static SlotContext Of(TileObjectInstance chest, Gameplay.Player.PlayerState? owner = null) =>
        GameWorld.Storage(chest, owner) is { } box ? new SlotContext(box, null, chest) : default;
}

/// <summary>The buttons of an open chest.</summary>
public enum ChestOp
{
    LootAll, DepositAll, QuickStack, Restock, Sort,
}

public sealed partial class GameWorld
{
    /// <summary>How far "quick stack to nearby chests" reaches (metres from the player's centre to a chest's centre).</summary>
    public const float QuickStackRadius = 12f;

    /// <summary>
    /// One click on an inventory slot, routed as the reference game does. With an empty cursor a left click with Alt
    /// toggles the favourite mark (backpack, coin and ammo slots), with Ctrl (no shop open) moves the stack to the trash
    /// slot (replacing what was there; favourites refuse) and with Shift quick-moves it: chest and backpack both ways,
    /// equip from the backpack, unequip to the backpack, sell while a shop is open, or into the service slot (reforge)
    /// while that is open. A right click opens a crate,
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

    /// <summary>
    /// Shift on a backpack item while a service slot is open (Terraria 1.4's reforge): the item goes into the slot if the
    /// service works on it, and whatever was in the slot takes its place in the backpack.
    /// </summary>
    private static ClickOutcome ToService(PlayerInventory inv, Inventory backpack, int index)
    {
        var s = backpack[index];
        if (SlotFilters.For(new SlotRef(SlotArea.Service, 0), inv) is { } accepts && !accepts(s.Item!)) return ClickOutcome.None;
        backpack[index] = inv.Service[0];
        inv.Service[0] = s;
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
                // Sold for its value (favourites and coins refuse) and kept in the shop's buyback slots.
                return Commerce.Sell(p, storage, i) == TradeResult.Ok ? ClickOutcome.Sold : ClickOutcome.None;
            case SlotArea.Backpack or SlotArea.Coins or SlotArea.Ammo when ctx.Container != null:
                storage[i] = ctx.Container.Add(s);
                break;
            case SlotArea.Backpack when ctx.Service:
                return ToService(inv, storage, i);
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

    /// <summary>
    /// Runs a chest button (<see cref="InventoryOps"/>) between the player and a placed chest, rolling its world loot
    /// first, and marks the chunk for saving when anything changed. Returns how many items moved (sorting: 0).
    /// </summary>
    public int ChestAction(PlayerEntity p, TileObjectInstance chest, ChestOp op)
    {
        if (Storage(chest, p.State) is not { } box) return 0;
        Editor.EnsureContainerLoot(chest);
        var inv = p.State.Inventory;
        int moved = op switch
        {
            ChestOp.LootAll => InventoryOps.LootAll(box, inv),
            ChestOp.DepositAll => InventoryOps.DepositAll(inv, box),
            ChestOp.QuickStack => InventoryOps.QuickStack(inv, box),
            ChestOp.Restock => InventoryOps.Restock(box, inv),
            _ => 0,
        };
        if (op == ChestOp.Sort) InventoryOps.SortChest(box);
        if (moved > 0 || op == ChestOp.Sort) MarkObjectModified(chest);
        return moved;
    }

    /// <summary>
    /// Quick Stack to Nearby Chests: every unlocked chest within <paramref name="radiusM"/> metres, nearest first,
    /// takes the backpack's non-favourite stacks (below the hotbar) of items it already holds. Returns the chests that
    /// received something and how many items each got (the screen can show them flying).
    /// </summary>
    public List<(TileObjectInstance Chest, int Moved)> QuickStackNearby(PlayerEntity p, float radiusM = QuickStackRadius)
    {
        var result = new List<(TileObjectInstance, int)>();
        var center = p.Center;
        var c = center * Units.CellsPerMeter;
        int r = (int)MathF.Ceiling(radiusM * Units.CellsPerMeter);
        var cell = new Int3((int)MathF.Floor(c.X), (int)MathF.Floor(c.Y), (int)MathF.Floor(c.Z));
        var near = new List<(TileObjectInstance Obj, float Distance)>();
        foreach (var obj in Session.World.TileObjects.InBox(Aabb3i.FromCenter(cell, new Int3(r, r, r))))
        {
            if (Storage(obj, p.State) == null || obj.Locked) continue;
            var b = obj.Bounds;
            var mid = new Vector3(b.Min.X + b.Max.X, b.Min.Y + b.Max.Y, b.Min.Z + b.Max.Z) * (0.5f * Units.CellSize);
            float d = Vector3.Distance(mid, center);
            if (d <= radiusM) near.Add((obj, d));
        }
        near.Sort((a, b) => a.Distance.CompareTo(b.Distance));
        foreach (var (obj, _) in near)
        {
            int moved = ChestAction(p, obj, ChestOp.QuickStack);
            if (moved > 0) result.Add((obj, moved));
        }
        return result;
    }

    /// <summary>
    /// Names a chest (trimmed, control characters dropped, at most <see cref="TileObjectInstance.MaxNameLength"/>
    /// characters; blank restores the default) and marks its chunk for saving. Returns false for objects without storage.
    /// </summary>
    public bool RenameContainer(TileObjectInstance chest, string? name)
    {
        if (chest.Container == null) return false; // personal storage keeps its own name
        chest.CustomName = name;
        MarkObjectModified(chest);
        return true;
    }

    /// <summary>Writes a sign's text (see <see cref="TileObjectInstance.SignText"/>) and marks its chunk for saving; false for other objects.</summary>
    public bool EditSign(TileObjectInstance sign, string? text)
    {
        if (sign.Def.Interaction != Content.Defs.TileInteraction.Read) return false;
        sign.SignText = text;
        Session.World.GetChunk(ChunkMath.CellToChunk(sign.Anchor))?.MarkModified();
        return true;
    }

    /// <summary>
    /// The slots a container object shows <paramref name="owner"/>: a chest's own, or for a Piggy Bank or Safe the
    /// character's personal storage of that kind (created on first use). Null for objects without storage.
    /// </summary>
    public static Inventory? Storage(TileObjectInstance obj, Gameplay.Player.PlayerState? owner) =>
        obj.Def.SharedStorage is { } kind
            ? owner?.Bank(kind)
            : obj.Container == null ? null : new Inventory(obj.Container);

    private void MarkObjectModified(TileObjectInstance obj)
    {
        if (obj.Container != null) Session.World.GetChunk(ChunkMath.CellToChunk(obj.Anchor))?.MarkModified();
    }
}
