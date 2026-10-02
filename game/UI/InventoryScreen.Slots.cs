using System;
using Godot;
using Strata.Core.Common;
using Strata.Core.Gameplay.Items;
using Strata.Core.Session;

namespace Strata.Game.UI;

/// <summary>
/// Slot clicks: every click goes to <see cref="GameWorld.ClickSlot"/> (right click equips, swaps with vanity, opens
/// crates or takes one; Ctrl trashes, Alt favourites, Shift quick-moves), a held right click keeps taking one at
/// <see cref="HoldRepeat"/>'s quickening pace, and a click outside every panel throws the held stack (right: one).
/// </summary>
public sealed partial class InventoryScreen
{
    private bool _showVanity;
    private ItemSlotView? _repeatView;
    private SlotRef _repeatSlot;
    private int _repeats;
    private double _repeatWait;

    /// <summary>The chest open beside the inventory, if any.</summary>
    private SlotContext Context => _container != null && _containerInv != null ? new SlotContext(_containerInv, null, _container) : SlotContext.None;

    private ItemSlotView Slot(SlotArea area, int index, string? badge = null) => Slot(() => new SlotRef(area, index), badge);

    /// <summary>An armor or accessory slot that shows the vanity row instead while the Vanity toggle is on.</summary>
    private ItemSlotView WornSlot(SlotArea worn, SlotArea vanity, int index) => Slot(() => new SlotRef(_showVanity ? vanity : worn, index), null);

    private ItemSlotView Slot(Func<SlotRef> slot, string? badge)
    {
        ItemSlotView view = null!;
        view = new ItemSlotView(() => Read(slot()), SlotSize, _ui.Tooltip) { Badge = badge, Favorite = () => IsFavorite(slot()) };
        view.Clicked += (button, mods) => Click(view, slot(), button == MouseButton.Right, mods);
        return view;
    }

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
        _repeatView = null;
        if (right && outcome == ClickOutcome.Moved)
        {
            _repeatView = view;
            _repeatSlot = slot;
            _repeats = 0;
            _repeatWait = HoldRepeat.IntervalTicks(0) / (double)Units.TicksPerSecond;
        }
        if (outcome != ClickOutcome.None) _crafting.Refresh();
    }

    /// <summary>Holding the right button on a slot keeps taking one, faster and faster, until released or moved off.</summary>
    private void RepeatRightClick(double delta)
    {
        if (_repeatView == null) return;
        if (!Input.IsMouseButtonPressed(MouseButton.Right) || !_repeatView.Hovered)
        {
            _repeatView = null;
            _crafting.Refresh();
            return;
        }
        _repeatWait -= delta;
        while (_repeatWait <= 0 && _repeatView != null)
        {
            _repeatWait += HoldRepeat.IntervalTicks(++_repeats) / (double)Units.TicksPerSecond;
            if (_ui.Game.ClickSlot(_ui.Entity, _repeatSlot, true, ClickMods.None, Context) != ClickOutcome.Moved) _repeatView = null;
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
    }
}
