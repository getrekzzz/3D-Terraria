using System;
using System.Collections.Generic;
using Godot;
using Strata.Core.Gameplay.Items;
using Strata.Core.Session;

namespace Strata.Game.UI;

/// <summary>
/// Slot views and clicks. Each slot is tinted by its area (<see cref="SlotKind"/>), shows its silhouette while empty
/// and the rounds left on bows and guns. Every click goes to <see cref="GameWorld.ClickSlot"/> (right click equips,
/// swaps with vanity, opens crates or takes one; Ctrl trashes, Alt favourites, Shift quick-moves); a held right click
/// keeps taking one at <see cref="HoldRepeat"/>'s quickening pace (the view repeats it while items keep moving); moving
/// items makes Terraria's grab sound; and a click outside every panel throws the held stack (right: one).
/// </summary>
public sealed partial class InventoryScreen
{
    private static readonly string[] ArmorHints = { "slot_head", "slot_body", "slot_legs" };
    private static readonly string[] MiscHints = { "slot_pet", "slot_light_pet", "slot_minecart", "slot_mount", "slot_hook" };
    private const double GrabSoundGap = 0.05;

    private readonly List<(Func<SlotRef> Slot, ItemSlotView View)> _slots = new();
    private bool _showVanity;
    private bool _craftingStale;
    private ulong _lastGrabMs;

    /// <summary>The chest open beside the inventory, if any.</summary>
    private SlotContext Context => _container != null && _containerInv != null ? new SlotContext(_containerInv, null, _container) : SlotContext.None;

    /// <summary>The view showing <paramref name="slot"/> now (the <c>ui hover</c> command), or null.</summary>
    public ItemSlotView? FindSlot(SlotRef slot)
    {
        foreach (var (at, view) in _slots)
            if (at() == slot && !view.IsQueuedForDeletion()) return view;
        return null;
    }

    private ItemSlotView Slot(SlotArea area, int index, string? badge = null) => Slot(() => new SlotRef(area, index), badge, Hint(area, index));

    /// <summary>An armor or accessory slot that shows the vanity row instead while the Vanity toggle is on.</summary>
    private ItemSlotView WornSlot(SlotArea worn, SlotArea vanity, int index) =>
        Slot(() => new SlotRef(_showVanity ? vanity : worn, index), null, Hint(worn, index));

    private ItemSlotView Slot(Func<SlotRef> slot, string? badge, Texture2D? hint)
    {
        ItemSlotView view = null!;
        view = new ItemSlotView(() => Read(slot()), SlotSize, KindOf(slot().Area), _ui.Tooltip)
        {
            Badge = badge,
            EmptyHint = hint,
            RepeatRight = true,
            Favorite = () => IsFavorite(slot()),
            AmmoCount = ammoClass => _ui.Inventory.CountAmmo(ammoClass),
        };
        view.Clicked += (button, mods) => Click(view, slot(), button == MouseButton.Right, mods);
        _slots.Add((slot, view));
        return view;
    }

    /// <summary>Drops the views of a container that was put away.</summary>
    private void ForgetFreedSlots() => _slots.RemoveAll(s => s.View.IsQueuedForDeletion());

    /// <summary>Terraria's slot tint per area: blue backpack, teal chest, violet equipment, darker vanity and dyes.</summary>
    private static SlotKind KindOf(SlotArea area) => area switch
    {
        SlotArea.Coins => SlotKind.Coin,
        SlotArea.Ammo => SlotKind.Ammo,
        SlotArea.Trash => SlotKind.Trash,
        SlotArea.Armor or SlotArea.Accessories => SlotKind.Equip,
        SlotArea.VanityArmor or SlotArea.VanityAccessories => SlotKind.Vanity,
        SlotArea.Dyes or SlotArea.MiscDyes => SlotKind.Dye,
        SlotArea.Misc => SlotKind.Misc,
        SlotArea.Container => SlotKind.Chest,
        SlotArea.Shop => SlotKind.Shop,
        _ => SlotKind.Inventory,
    };

    /// <summary>The silhouette an empty slot shows (generated UI sprites), or null.</summary>
    private static Texture2D? Hint(SlotArea area, int index) => area switch
    {
        SlotArea.Armor or SlotArea.VanityArmor when index < ArmorHints.Length => UiSprites.Get(ArmorHints[index]),
        SlotArea.Accessories or SlotArea.VanityAccessories => UiSprites.Get("slot_accessory"),
        SlotArea.Dyes or SlotArea.MiscDyes => UiSprites.Get("slot_dye"),
        SlotArea.Misc when index < MiscHints.Length => UiSprites.Get(MiscHints[index]),
        SlotArea.Coins => UiSprites.Get("slot_coin"),
        SlotArea.Ammo => UiSprites.Get("slot_ammo"),
        SlotArea.Trash => UiSprites.Get("trash"),
        _ => null,
    };

    private ItemStack Read(SlotRef slot)
    {
        var inv = slot.Area == SlotArea.Container ? _containerInv : _ui.Inventory.Resolve(slot.Area);
        return inv != null && slot.Index < inv.Size ? inv[slot.Index] : ItemStack.Empty;
    }

    private bool IsFavorite(SlotRef slot) =>
        slot.Area != SlotArea.Container && _ui.Inventory.Resolve(slot.Area) is { } inv && slot.Index < inv.Size && inv.IsFavorite(slot.Index);

    private void Click(ItemSlotView view, SlotRef slot, bool right, ClickMods mods)
    {
        var outcome = _ui.Game.ClickSlot(_ui.Entity, slot, right, mods, Context);
        PlaySound(outcome);
        // Only taking items one at a time repeats; equipping, opening a crate or a refused take ends the hold.
        if (right && outcome != ClickOutcome.Moved) view.StopRepeat();
        if (outcome == ClickOutcome.None) return;
        if (view.Repeating) _craftingStale = true;
        else _crafting.Refresh();
    }

    /// <summary>Terraria's grab for items moving, a tick for a favourite mark, coins for a sale.</summary>
    private void PlaySound(ClickOutcome outcome)
    {
        switch (outcome)
        {
            case ClickOutcome.Moved or ClickOutcome.Equipped or ClickOutcome.Trashed or ClickOutcome.Opened:
                // A fast hold-repeat takes up to one item a frame: one grab per 50 ms is enough.
                ulong now = Time.GetTicksMsec();
                if (now - _lastGrabMs < GrabSoundGap * 1000) return;
                _lastGrabMs = now;
                Audio.Sfx.Play("grab");
                break;
            case ClickOutcome.Favorited:
                Audio.Sfx.Play("ui_click");
                break;
            case ClickOutcome.Sold:
                Audio.Sfx.Play("coins");
                break;
        }
    }

    /// <summary>A click that no panel took, with a stack on the cursor, throws it where the player looks (right click: one).</summary>
    private bool ThrowCursor(InputEventMouseButton mb)
    {
        if (mb.ButtonIndex is not (MouseButton.Left or MouseButton.Right) || _ui.Inventory.Cursor.IsEmpty) return false;
        _ui.Game.ThrowCursor(_ui.Entity, one: mb.ButtonIndex == MouseButton.Right);
        _crafting.Refresh();
        return true;
    }

    private void ToggleVanity()
    {
        _showVanity = !_showVanity;
        _vanityToggle.Text = _showVanity ? "Worn" : "Vanity";
        _vanityToggle.TooltipText = _showVanity ? "Showing the vanity row: looks only, no stats" : "Show the vanity armor and accessories";
        foreach (var (slot, view) in _slots) view.Kind = KindOf(slot().Area);
    }
}
