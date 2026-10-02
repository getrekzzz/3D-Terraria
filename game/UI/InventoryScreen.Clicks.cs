using Godot;
using Strata.Core.Gameplay.Items;
using Strata.Core.Session;

namespace Strata.Game.UI;

/// <summary>
/// Clicks: every slot click goes to <see cref="GameWorld.ClickSlot"/> with the open chest as its context (right click
/// equips, swaps with vanity, opens crates or takes one; Ctrl trashes, Alt favourites, Shift quick-moves); a held right
/// click keeps taking one at <see cref="HoldRepeat"/>'s quickening pace (the view repeats it while items keep moving).
/// Moving, equipping, trashing and opening make Terraria's grab sound, a favourite mark a tick and a sale coins (the
/// crafting list follows the backpack through its change events). The Sort Inventory and Quick Stack to Nearby Chests
/// buttons live here too. A click outside every panel throws the held stack (<see cref="WorldClickCatcher"/>).
/// </summary>
public sealed partial class InventoryScreen
{
    private const double GrabSoundGap = 0.05;

    private ulong _lastGrabMs;

    /// <summary>The chest open under the inventory (Shift moves to it), or the shop (Shift sells), if any.</summary>
    private SlotContext Context =>
        _chest.Container is { } chest && _chest.ContainerInventory is { } inv ? new SlotContext(inv, null, chest)
        : _panel == _shop && _shop.Npc is { } npc ? new SlotContext(Shop: npc)
        : SlotContext.None;

    private void Click(ItemSlotView view, SlotRef slot, bool right, ClickMods mods)
    {
        var outcome = PressSlot(slot, right, mods);
        // Only taking items one at a time repeats; equipping, opening a crate or a refused take ends the hold.
        if (right && outcome != ClickOutcome.Moved) view.StopRepeat();
    }

    /// <summary>One click on a slot as the player makes it, with its sound (the views and the <c>ui sell</c> command).</summary>
    public ClickOutcome PressSlot(SlotRef slot, bool right, ClickMods mods)
    {
        var outcome = _ui.Game.ClickSlot(_ui.Entity, slot, right, mods, Context);
        PlaySound(outcome);
        return outcome;
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

    /// <summary>Sort Inventory: the backpack below the hotbar, by kind and value (favourites stay put).</summary>
    public void Sort()
    {
        InventoryOps.Sort(_ui.Inventory.Backpack, PlayerInventory.HotbarSize, PlayerInventory.BackpackSize);
        Audio.Sfx.Play("grab");
    }

    /// <summary>Quick Stack to Nearby Chests: items the chests in reach already hold leave the backpack. Returns the chests filled.</summary>
    public int QuickStack()
    {
        int chests = _ui.Game.QuickStackNearby(_ui.Entity).Count;
        if (chests == 0) return 0;
        Audio.Sfx.Play("grab");
        return chests;
    }
}
