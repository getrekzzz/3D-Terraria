using System;
using Godot;
using Strata.Core.Gameplay.Items;
using Strata.Core.Presentation;

namespace Strata.Game.UI;

/// <summary>
/// Building the screen's parts and placing them on the layout's regions: the backpack grid (<see cref="UiRegion.Inventory"/>,
/// on the HUD hotbar's origin), the coin and ammo columns, trash and sort buttons (<see cref="CoinAmmoColumns"/>), the
/// panel host (<see cref="UiRegion.Panel"/>), the crafting column and the equipment column. The HUD computes the layout
/// from <see cref="Panel"/> and <see cref="AccessoryRows"/> and calls <see cref="Place"/> when it changes (the window,
/// the UI scale, opening, a panel shown or closed, an accessory row appearing), never per frame. Slots are sized by the
/// layout's slot scale.
/// </summary>
public sealed partial class InventoryScreen
{
    private readonly ItemSlotView[] _backpackViews = new ItemSlotView[PlayerInventory.BackpackSize];
    private readonly ItemSlotView[] _hotbarViews = new ItemSlotView[PlayerInventory.HotbarSize];
    private Control _backpack = null!;
    private CoinAmmoColumns _coinAmmo = null!;
    private Control _panelHost = null!;
    private Control? _panel;
    private CraftingPanel _crafting = null!;
    private EquipmentPanel _equipment = null!;

    /// <summary>
    /// The layout inputs changed (opened or closed, a panel shown or closed, an accessory row appeared): the HUD lays
    /// the screen out again at once.
    /// </summary>
    public event Action? LayoutChanged;

    /// <summary>The panel shown under the grid (chest, shop, service), or <see cref="UiPanel.None"/>.</summary>
    public UiPanel Panel { get; private set; }

    /// <summary>Accessory rows of the equipment column: the slots the world unlocked, and a locked one still holding an item.</summary>
    public int AccessoryRows => _equipment.AccessoryRows;

    /// <summary>The equipment column (tabs, Dye | Vanity | Equip, Misc page, defence shield).</summary>
    public EquipmentPanel Equipment => _equipment;

    /// <summary>The layout's slot scale (1, or smaller on a crowded screen); hosted panels size their slots by it.</summary>
    public float SlotScale { get; private set; } = 1f;

    /// <summary>The backpack grid is shown with its first row on the HUD hotbar's spot (the <c>ui.grid_on_hotbar</c> probe).</summary>
    public bool GridOnHotbar => _backpack.IsVisibleInTree() && _backpack.Position == new Vector2(UiLayout.Margin, UiLayout.HotbarY)
        && _hotbarViews[0].Position == Vector2.Zero;

    private void BuildLayout()
    {
        _backpack = Region("Inventory");
        for (int i = 0; i < PlayerInventory.BackpackSize; i++)
        {
            var view = Slot(SlotArea.Backpack, i, i < PlayerInventory.HotbarSize ? ((i + 1) % 10).ToString() : null);
            if (i < PlayerInventory.HotbarSize) _hotbarViews[i] = view;
            _backpackViews[i] = view;
            _backpack.AddChild(view);
        }
        _coinAmmo = new CoinAmmoColumns((area, index) => Slot(area, index), Sort, () => QuickStack());
        _root.AddChild(LayoutWidgets.Register(_coinAmmo.Columns));
        _root.AddChild(LayoutWidgets.Register(_coinAmmo.Trash));
        _root.AddChild(LayoutWidgets.Register(_coinAmmo.Buttons));

        _panelHost = Region("Panel");
        _panelHost.Visible = false;
        BuildChestPanel();

        _crafting = LayoutWidgets.Register(new CraftingPanel { Name = "Crafting" });
        _root.AddChild(_crafting);
        _crafting.Initialize(_ui);

        _equipment = LayoutWidgets.Register(new EquipmentPanel((area, index) => Slot(area, index)));
        _root.AddChild(_equipment);
        _equipment.RowsChanged += () =>
        {
            if (IsOpen) LayoutChanged?.Invoke();
        };
    }

    /// <summary>A registered region widget that takes the clicks between its slots (no throw from a gap).</summary>
    private Control Region(string name)
    {
        var c = LayoutWidgets.Register(new Control { Name = name, MouseFilter = Control.MouseFilterEnum.Stop });
        _root.AddChild(c);
        return c;
    }

    /// <summary>Moves every part onto its region of <paramref name="layout"/>; parts without one are hidden.</summary>
    public void Place(UiLayoutResult layout)
    {
        _layout = layout;
        SlotScale = layout.SlotScale;
        if (LayoutWidgets.Place(_backpack, layout, UiRegion.Inventory))
        {
            _backpack.Visible = true;
            float side = UiLayout.Slot * SlotScale;
            for (int i = 0; i < _backpackViews.Length; i++)
                PutSlot(_backpackViews[i], i % 10 * (side + UiLayout.Gap), i / 10 * (side + UiLayout.Gap), side);
        }
        else _backpack.Visible = false;
        _coinAmmo.Place(layout);
        PlacePanel();
        _crafting.Visible = LayoutWidgets.Place(_crafting, layout, UiRegion.Crafting);
        _equipment.Visible = LayoutWidgets.Place(_equipment, layout, UiRegion.Equipment);
        if (_equipment.Visible) _equipment.Arrange(SlotScale);
    }

    /// <summary>
    /// Shows <paramref name="panel"/> under the grid in place of the current one (a chest closes): it fills the Panel
    /// region, whose size the layout gives for <paramref name="kind"/>, and sizes its slots by <see cref="SlotScale"/>.
    /// </summary>
    public void ShowPanel(Control panel, UiPanel kind)
    {
        if (_panel != panel) HidePanel();
        if (panel.GetParent() != _panelHost)
        {
            panel.GetParent()?.RemoveChild(panel);
            _panelHost.AddChild(panel);
        }
        panel.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        panel.Visible = true;
        _panel = panel;
        Panel = kind;
        PlacePanel(); // on the current layout until the HUD lays the screen out for this kind of panel
        LayoutChanged?.Invoke();
    }

    /// <summary>Closes the panel under the grid (a chest's lid shuts).</summary>
    public void ClosePanel()
    {
        if (_panel == null) return;
        HidePanel();
        LayoutChanged?.Invoke();
    }

    private void HidePanel()
    {
        if (_panel == null) return;
        var panel = _panel;
        _panel = null;
        Panel = UiPanel.None;
        panel.Visible = false;
        _panelHost.Visible = false;
        if (panel == _chestPanel) ReleaseContainer();
    }

    /// <summary>Positions and sizes a slot inside its region (its minimum size too, so it can shrink).</summary>
    private static void PutSlot(ItemSlotView view, float x, float y, float side)
    {
        view.Position = new Vector2(x, y);
        view.CustomMinimumSize = view.Size = new Vector2(side, side);
    }
}
