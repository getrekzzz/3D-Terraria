using Godot;
using Strata.Core.Gameplay.Items;

namespace Strata.Game.UI;

/// <summary>
/// The line above the hotbar, as in Terraria: the held stack's name with its prefix and count ("Legendary Muramasa",
/// "Torch (25)") in its rarity colour (Expert and Master tiers cycle), centred over the bar and always shown; with the
/// inventory open it reads "Inventory" over the grid's left edge. The text is rebuilt only when the held stack changes.
/// </summary>
public sealed partial class ItemNameLabel : OutlinedText
{
    private readonly UiContext _ui;
    private ItemStack _shown;
    private bool _inventory;
    private bool _stale = true;
    private bool _animated;

    public ItemNameLabel(UiContext ui)
    {
        _ui = ui;
        Name = "ItemName";
    }

    /// <summary>Shows "Inventory" (open) or the held item's name (closed).</summary>
    public bool InventoryOpen
    {
        set
        {
            if (_inventory == value) return;
            _inventory = value;
            _stale = true; // the held item's name comes back on the next frame
            if (!value) return;
            Text = "Inventory";
            Align = HorizontalAlignment.Left;
            SelfModulate = UiTheme.Text;
            _animated = false;
        }
    }

    public override void _Process(double delta)
    {
        if (_inventory) return;
        var held = _ui.Inventory.Held;
        if (_stale || !held.Equals(_shown))
        {
            _stale = false;
            _shown = held;
            Align = HorizontalAlignment.Center;
            Text = held.IsEmpty ? "" : ItemTooltip.NameLine(held);
            _animated = !held.IsEmpty && UiTheme.IsAnimated(held.Item!.Rarity);
            // White text and a black outline, tinted as a whole: the outline stays black.
            SelfModulate = held.IsEmpty ? UiTheme.Text : UiTheme.Rarity(held.Item!.Rarity);
        }
        else if (_animated) SelfModulate = UiTheme.Rarity(held.Item!.Rarity);
    }
}
