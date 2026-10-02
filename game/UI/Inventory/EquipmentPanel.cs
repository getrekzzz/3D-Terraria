using System;
using Godot;
using Strata.Core.Gameplay.Items;
using Strata.Core.Gameplay.Player;
using Strata.Core.Presentation;

namespace Strata.Game.UI;

/// <summary>
/// The equipment column on the layout's Equipment region, under the minimap, laid out as Terraria's: a row of tabs
/// (Equipment, Misc, and Housing, which the town phase fills) over the pages, and the defence shield
/// (<see cref="DefenseBadge"/>) left of them. The Equipment page has the Dye | Vanity | Equip columns for the helmet,
/// shirt and pants and for each accessory row the world unlocks (5–7, <c>GameWorld.AccessorySlotsFor</c>); a locked row
/// still holding an item shows greyed until it is emptied, and every accessory row has an eye that hides its item on
/// the character (<see cref="PlayerInventory.HiddenAccessories"/>, read by <see cref="PlayerInventory.ShownAccessories"/>).
/// The Misc page (EquipmentPanel.Misc.cs) holds the pet, light pet, minecart, mount and hook with their dyes. The slots
/// come from the screen, which routes their clicks to <c>GameWorld.ClickSlot</c>; Core recomputes the stats.
/// </summary>
public sealed partial class EquipmentPanel : Control
{
    /// <summary>Rows of the Equipment page: helmet, shirt, pants, then the accessory rows.</summary>
    private const int Rows = PlayerInventory.ArmorSlots + PlayerInventory.MaxAccessorySlots;

    private static readonly string[] ArmorNames = { "Helmet", "Shirt", "Pants" };
    private static readonly Color TabIdle = new(0.55f, 0.55f, 0.62f);
    private static readonly Color TabHover = new(1.2f, 1.2f, 1.1f);

    private readonly Func<SlotArea, int, ItemSlotView> _slot;
    private readonly ItemSlotView[] _dyes = new ItemSlotView[Rows];
    private readonly ItemSlotView[] _vanity = new ItemSlotView[Rows];
    private readonly ItemSlotView[] _equip = new ItemSlotView[Rows];
    private readonly bool[] _rowShown = new bool[Rows];
    private readonly TextureButton[] _eyes = new TextureButton[PlayerInventory.MaxAccessorySlots];
    private readonly Control _equipPage = new() { Name = "EquipmentPage", MouseFilter = MouseFilterEnum.Ignore };
    private readonly TextureButton _equipTab, _miscTab, _housingTab;
    private readonly Texture2D _eyeOpen = UiSprites.Get("eye_open"), _eyeClosed = UiSprites.Get("eye_closed");
    private PlayerInventory? _inv;
    private int _hiddenShown = -1;
    private float _scale = 1f;

    /// <param name="slot">Makes the view of a slot (the screen routes its clicks and draws its silhouette).</param>
    public EquipmentPanel(Func<SlotArea, int, ItemSlotView> slot)
    {
        Name = "Equipment";
        MouseFilter = MouseFilterEnum.Stop; // the gaps between slots take clicks too (no throw from a gap)
        _slot = slot;
        AddChild(Defense = new DefenseBadge());
        AddChild(_equipTab = Tab("tab_equipment", "Equipment", () => ShowMisc(false)));
        AddChild(_miscTab = Tab("tab_misc", "Misc", () => ShowMisc(true)));
        AddChild(_housingTab = Tab("tab_housing", "Housing (opens with the town's residents)", () => { }));
        _housingTab.Disabled = true;
        _housingTab.SelfModulate = new Color(1, 1, 1, 0.35f);
        AddChild(_equipPage);
        for (int r = 0; r < Rows; r++)
        {
            bool armor = r < PlayerInventory.ArmorSlots;
            int i = armor ? r : r - PlayerInventory.ArmorSlots;
            string name = armor ? ArmorNames[r] : "Accessory";
            _equipPage.AddChild(_dyes[r] = Named(SlotArea.Dyes, r, "Dye"));
            _equipPage.AddChild(_vanity[r] = Named(armor ? SlotArea.VanityArmor : SlotArea.VanityAccessories, i, "Social " + name));
            _equipPage.AddChild(_equip[r] = Named(armor ? SlotArea.Armor : SlotArea.Accessories, i, name));
            _rowShown[r] = armor || i < PlayerInventory.BaseAccessorySlots;
            if (armor) continue;
            int accessory = i;
            var eye = _eyes[i] = new TextureButton
            {
                Name = $"Eye{i}", TextureNormal = _eyeOpen, FocusMode = FocusModeEnum.None, TextureFilter = TextureFilterEnum.Nearest,
                IgnoreTextureSize = true, StretchMode = TextureButton.StretchModeEnum.KeepCentered,
                TooltipText = "Show or hide this accessory on your character",
            };
            eye.Pressed += () => ToggleHidden(accessory);
            _equipPage.AddChild(eye);
        }
        BuildMisc();
        ShowMisc(false);
    }

    /// <summary>The defence shield left of the columns.</summary>
    public DefenseBadge Defense { get; }

    /// <summary>The Misc page is shown (else the Equipment page).</summary>
    public bool MiscShown => _miscPage.Visible;

    /// <summary>Accessory rows on the Equipment page: the unlocked ones, and locked ones still holding an item.</summary>
    public int AccessoryRows { get; private set; } = PlayerInventory.BaseAccessorySlots;

    /// <summary>Raised when <see cref="AccessoryRows"/> changes: the screen is laid out again for the new height.</summary>
    public event Action? RowsChanged;

    /// <summary>Shows the Misc page (pet, light pet, minecart, mount, hook) or the Equipment page.</summary>
    public void ShowMisc(bool misc)
    {
        if (_miscPage.Visible == misc && _equipPage.Visible == !misc) return;
        _miscPage.Visible = misc;
        _equipPage.Visible = !misc;
        _equipTab.SelfModulate = misc ? TabIdle : Colors.White;
        _miscTab.SelfModulate = misc ? Colors.White : TabIdle;
        Arrange(_scale);
    }

    /// <summary>
    /// Follows <paramref name="state"/> while the inventory is open: the defence, which accessory rows show (and lock),
    /// and the eyes. Cheap enough per frame: it reads 7 rows and lays out only when a row appears or goes.
    /// </summary>
    public void Refresh(PlayerState state)
    {
        Defense.Update(state);
        var inv = _inv = state.Inventory;
        bool changed = false;
        int rows = 0;
        for (int i = 0; i < PlayerInventory.MaxAccessorySlots; i++)
        {
            int r = PlayerInventory.ArmorSlots + i;
            bool locked = inv.IsAccessorySlotLocked(i);
            bool shown = !locked || !inv.Accessories[i].IsEmpty || !inv.VanityAccessories[i].IsEmpty || !inv.Dyes[r].IsEmpty;
            if (shown) rows++;
            _dyes[r].Locked = _vanity[r].Locked = _equip[r].Locked = locked;
            _eyes[i].Disabled = locked;
            if (_rowShown[r] == shown) continue;
            _rowShown[r] = shown;
            changed = true;
        }
        if (inv.HiddenAccessories != _hiddenShown)
        {
            _hiddenShown = inv.HiddenAccessories;
            for (int i = 0; i < _eyes.Length; i++)
            {
                bool hidden = inv.IsAccessoryHidden(i);
                _eyes[i].TextureNormal = hidden ? _eyeClosed : _eyeOpen;
                _eyes[i].SelfModulate = hidden ? TabIdle : Colors.White;
            }
        }
        if (changed) Arrange(_scale);
        if (rows == AccessoryRows) return;
        AccessoryRows = rows;
        RowsChanged?.Invoke();
    }

    /// <summary>
    /// Lays the column out for a slot scale (<see cref="UiLayout.EquipmentWidth"/>): the tabs over the columns, the
    /// shown rows packed from the top (a hidden locked row leaves no gap), the eyes right of the Equip column and the
    /// shield left of the last row.
    /// </summary>
    public void Arrange(float scale)
    {
        _scale = scale;
        float side = UiLayout.EquipSlot * scale, pitch = side + UiLayout.Gap, tab = UiLayout.SortButton;
        float dyeX = UiLayout.DefenseBadge + UiLayout.Gap, top = tab + UiLayout.Gap;
        float vanityX = dyeX + pitch, equipX = vanityX + pitch;
        _equipTab.Position = new Vector2(dyeX, 0);
        _miscTab.Position = new Vector2(dyeX + tab + UiLayout.Gap, 0);
        _housingTab.Position = new Vector2(dyeX + 2 * (tab + UiLayout.Gap), 0);
        int row = 0;
        for (int r = 0; r < Rows; r++)
        {
            bool shown = _rowShown[r];
            _dyes[r].Visible = _vanity[r].Visible = _equip[r].Visible = shown;
            if (r >= PlayerInventory.ArmorSlots) _eyes[r - PlayerInventory.ArmorSlots].Visible = shown;
            if (!shown) continue;
            float y = top + row++ * pitch;
            Put(_dyes[r], dyeX, y, side);
            Put(_vanity[r], vanityX, y, side);
            Put(_equip[r], equipX, y, side);
            if (r < PlayerInventory.ArmorSlots) continue;
            var eye = _eyes[r - PlayerInventory.ArmorSlots];
            eye.Position = new Vector2(equipX + side + UiLayout.Gap, y);
            eye.Size = new Vector2(UiLayout.EyeToggle, side);
        }
        int pageRows = MiscShown ? MiscSlots.Count : row;
        ArrangeMisc(vanityX, equipX, top, side, pitch);
        Defense.Position = new Vector2(0, top + pageRows * pitch - UiLayout.Gap - UiLayout.DefenseBadge);
    }

    /// <summary>Presses an accessory row's eye (the <c>ui eye</c> command); false for a row that is locked or not shown.</summary>
    public bool PressEye(int accessory)
    {
        if ((uint)accessory >= (uint)_eyes.Length || !_eyes[accessory].IsVisibleInTree() || _eyes[accessory].Disabled) return false;
        ToggleHidden(accessory);
        return true;
    }

    private void ToggleHidden(int accessory)
    {
        if (_inv == null) return;
        _inv.SetAccessoryHidden(accessory, !_inv.IsAccessoryHidden(accessory));
        Audio.Sfx.Play("ui_click");
    }

    private ItemSlotView Named(SlotArea area, int index, string name)
    {
        var view = _slot(area, index);
        view.EmptyText = name;
        return view;
    }

    private static void Put(ItemSlotView view, float x, float y, float side)
    {
        view.Position = new Vector2(x, y);
        view.Resize(side);
    }

    /// <summary>A 26 px pixel-art tab; it never takes keyboard focus (Space jumps) and brightens under the mouse.</summary>
    private static TextureButton Tab(string sprite, string tooltip, Action pressed)
    {
        var b = new TextureButton
        {
            Name = sprite, TextureNormal = UiSprites.Get(sprite), TooltipText = tooltip, FocusMode = FocusModeEnum.None,
            TextureFilter = TextureFilterEnum.Nearest, IgnoreTextureSize = true, StretchMode = TextureButton.StretchModeEnum.KeepCentered,
            Size = new Vector2(UiLayout.SortButton, UiLayout.SortButton),
        };
        b.MouseEntered += () => b.Modulate = b.Disabled ? Colors.White : TabHover;
        b.MouseExited += () => b.Modulate = Colors.White;
        b.Pressed += () =>
        {
            pressed();
            Audio.Sfx.Play("ui_click");
        };
        return b;
    }
}
