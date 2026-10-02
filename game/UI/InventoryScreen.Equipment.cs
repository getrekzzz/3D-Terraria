using Godot;
using Strata.Core.Gameplay.Items;
using Strata.Core.Presentation;

namespace Strata.Game.UI;

/// <summary>
/// The equipment column in the layout's Equipment region, under the minimap: the defence shield (its tooltip names the
/// set bonus), the misc slots (pet, light pet, minecart, mount, hook) and, in the right column, the armour and the
/// accessory slots the world unlocks, which the Vanity toggle swaps for the vanity row. P2-16 replaces it with
/// Terraria's Dye | Vanity | Equip columns and tabs.
/// </summary>
public sealed partial class InventoryScreen
{
    private readonly ItemSlotView[] _armorViews = new ItemSlotView[PlayerInventory.ArmorSlots];
    private readonly ItemSlotView[] _accessoryViews = new ItemSlotView[PlayerInventory.MaxAccessorySlots];
    private readonly ItemSlotView[] _miscViews = new ItemSlotView[MiscSlots.Count];
    private Control _equipment = null!;
    private OutlinedText _equipCaption = null!;
    private Button _vanityToggle = null!;
    private TextureRect _defenseBadge = null!;
    private OutlinedText _defenseText = null!;
    private int _defenseShown = -1;
    private string? _setBonusShown;

    private void BuildEquipment()
    {
        _equipment = Region("Equipment");
        _equipCaption = new OutlinedText { Text = "Equipment", FontSize = UiFonts.Small, Align = HorizontalAlignment.Left };
        _equipment.AddChild(_equipCaption);
        _vanityToggle = TextButton("Vanity", ToggleVanity, "Show the vanity armor and accessories");
        _equipment.AddChild(_vanityToggle);
        _defenseBadge = new TextureRect
        {
            Texture = UiSprites.Get("shield"), StretchMode = TextureRect.StretchModeEnum.KeepCentered,
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest, MouseFilter = Control.MouseFilterEnum.Stop,
        };
        _defenseText = new OutlinedText { FontSize = UiFonts.Small };
        _defenseText.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _defenseBadge.AddChild(_defenseText);
        _equipment.AddChild(_defenseBadge);
        for (int i = 0; i < _armorViews.Length; i++) _equipment.AddChild(_armorViews[i] = WornSlot(SlotArea.Armor, SlotArea.VanityArmor, i));
        for (int i = 0; i < _accessoryViews.Length; i++)
            _equipment.AddChild(_accessoryViews[i] = WornSlot(SlotArea.Accessories, SlotArea.VanityAccessories, i));
        for (int i = 0; i < _miscViews.Length; i++) _equipment.AddChild(_miscViews[i] = Slot(SlotArea.Misc, i));
    }

    /// <summary>
    /// The column's grid (<see cref="UiLayout.EquipmentWidth"/>): the shield, an empty dye column, misc in the middle
    /// column and the worn slots on the right, under a header row with the caption and the Vanity toggle.
    /// </summary>
    private void ArrangeEquipment()
    {
        float side = UiLayout.EquipSlot * SlotScale, pitch = side + UiLayout.Gap;
        float misc = UiLayout.DefenseBadge + UiLayout.Gap + pitch, worn = misc + pitch;
        float header = UiLayout.SortButton, top = header + UiLayout.Gap;
        _equipCaption.Position = Vector2.Zero;
        _equipCaption.Size = new Vector2(worn - UiLayout.Gap, header);
        _vanityToggle.Position = new Vector2(worn, 0);
        _vanityToggle.Size = new Vector2(_equipment.Size.X - worn, header);
        _defenseBadge.Position = new Vector2(0, top);
        _defenseBadge.Size = new Vector2(UiLayout.DefenseBadge, UiLayout.DefenseBadge);
        for (int i = 0; i < _armorViews.Length; i++) PutSlot(_armorViews[i], worn, top + i * pitch, side);
        for (int i = 0; i < _accessoryViews.Length; i++) PutSlot(_accessoryViews[i], worn, top + (_armorViews.Length + i) * pitch, side);
        for (int i = 0; i < _miscViews.Length; i++) PutSlot(_miscViews[i], misc, top + i * pitch, side);
    }

    /// <summary>
    /// The defence number and set bonus (redrawn on change), and the accessory slots: Expert and Master worlds unlock
    /// the 6th and 7th; a locked slot still shows an item left in it. A new row lays the screen out again.
    /// </summary>
    private void UpdateEquipment()
    {
        var state = _ui.Entity.State;
        if (state.Defense != _defenseShown || state.SetBonus != _setBonusShown)
        {
            _defenseShown = state.Defense;
            _setBonusShown = state.SetBonus;
            _defenseText.Text = _defenseShown.ToString();
            _defenseBadge.TooltipText = _setBonusShown != null ? $"{_defenseShown} Defense\nSet bonus: {_setBonusShown}" : $"{_defenseShown} Defense";
        }
        var inv = state.Inventory;
        var row = _showVanity ? inv.VanityAccessories : inv.Accessories;
        int rows = PlayerInventory.BaseAccessorySlots;
        for (int i = 0; i < _accessoryViews.Length; i++)
        {
            bool unlocked = i < inv.AccessorySlotsUnlocked, shown = unlocked || !row[i].IsEmpty;
            _accessoryViews[i].Visible = shown;
            _accessoryViews[i].Locked = !unlocked;
            if (shown) rows = Mathf.Max(rows, i + 1);
        }
        if (rows == AccessoryRows) return;
        AccessoryRows = rows;
        if (IsOpen) LayoutChanged?.Invoke();
    }

    private void ToggleVanity()
    {
        _showVanity = !_showVanity;
        _vanityToggle.Text = _showVanity ? "Worn" : "Vanity";
        _vanityToggle.TooltipText = _showVanity ? "Showing the vanity row: looks only, no stats" : "Show the vanity armor and accessories";
        foreach (var (slot, view) in _slots) view.Kind = KindOf(slot().Area);
        UpdateEquipment();
    }
}
