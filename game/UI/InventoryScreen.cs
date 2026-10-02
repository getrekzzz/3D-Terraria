using System;
using Godot;
using Strata.Core.Gameplay.Items;
using Strata.Core.Presentation;
using Strata.Core.Voxels.TileObjects;

namespace Strata.Game.UI;

/// <summary>
/// The inventory screen (Tab/I), laid out like Terraria's on the HUD's <see cref="UiLayout"/>: the 10×5 backpack on
/// the hotbar's spot (its first row is the hotbar), the coin and ammo columns right of it with the trash under them and
/// the Sort Inventory and Quick Stack to Nearby Chests buttons beside them, the panel host under the grid (a chest
/// today; shops and services plug in through <see cref="ShowPanel"/>), the crafting column bottom-left and the
/// equipment column (<see cref="EquipmentPanel"/>) on the right under the minimap. Each part is a registered layout
/// widget placed by <see cref="Place"/> (InventoryScreen.Layout.cs); items move with a cursor stack drawn at the
/// pointer by <see cref="CursorItemLayer"/> and clicks are routed by <c>GameWorld.ClickSlot</c>
/// (InventoryScreen.Clicks.cs). An overlay: the player keeps moving, and clicks on empty screen go to the
/// <see cref="WorldClickCatcher"/> under every panel.
/// </summary>
public sealed partial class InventoryScreen : CanvasLayer
{
    private const string Gate = "inventory";
    private UiContext _ui = null!;
    private Control _root = null!;

    public bool IsOpen => _root.Visible;

    /// <summary>A container's contents were shown (true) or put away (false); its lid follows.</summary>
    public event Action<TileObjectInstance, bool>? ContainerShown;

    public void Initialize(UiContext ui)
    {
        _ui = ui;
        Name = "Inventory";
        Layer = 20;
        _root = new Control { Visible = false, Theme = UiTheme.Theme, MouseFilter = Control.MouseFilterEnum.Ignore };
        _root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_root);
        BuildLayout();
        ui.Game.Interaction.ContainerOpened += (p, obj) =>
        {
            if (p == ui.Entity) OpenChest(obj);
        };
    }

    public override void _UnhandledInput(InputEvent e)
    {
        // Clicks on empty screen (throwing the held stack, using the item, turning the camera) go to WorldClickCatcher.
        if (InputGate.TextEntry || (!IsOpen && InputGate.GameplayBlocked)) return;
        if (e.IsActionPressed("inventory") || (IsOpen && e.IsActionPressed("pause")))
        {
            SetOpen(!IsOpen);
            GetViewport().SetInputAsHandled();
        }
    }

    public void SetOpen(bool open)
    {
        if (open == IsOpen) return;
        _root.Visible = open;
        Audio.Sfx.Play(open ? "ui_open" : "ui_close");
        // An overlay: the player keeps walking, jumping and fighting. Auto-pause (single player option): the world waits
        // while the inventory is open, and so does the player, so it is a modal then.
        bool autoPause = Settings.UserSettings.Current.AutoPause;
        InputGate.SetOpen(Gate, open, autoPause ? GateKind.Modal : GateKind.Overlay);
        if (autoPause || !open) _ui.Player.Runtime.Simulating = !open;
        if (open)
        {
            _equipment.Refresh(_ui.Entity.State);
            _crafting.Refresh();
            LayoutChanged?.Invoke();
            return;
        }
        // The held stack and the service slot's item (a reforge) go back into the storage (coins and ammo to their
        // slots); what does not fit is thrown.
        var inv = _ui.Inventory;
        var held = inv.Cursor;
        inv.Cursor = ItemStack.Empty;
        Stow(held);
        var serviced = inv.Service[0];
        inv.Service[0] = ItemStack.Empty;
        Stow(serviced);
        _craftingStale = false;
        HidePanel();
        LayoutChanged?.Invoke();
    }

    public override void _Process(double delta)
    {
        using var profile = Strata.Core.Diagnostics.Metrics.Frame.Time("gd.inventory_ui");
        if (!IsOpen) return;
        // Taking one at a time with a held right click refreshes the crafting list once, when the button comes up.
        if (_craftingStale && !Input.IsMouseButtonPressed(MouseButton.Right))
        {
            _craftingStale = false;
            _crafting.Refresh();
        }
        _equipment.Refresh(_ui.Entity.State);
        int selected = _ui.Inventory.SelectedHotbar;
        for (int i = 0; i < _hotbarViews.Length; i++) _hotbarViews[i].Selected = selected == i;
    }

    /// <summary>
    /// Shows a chest the player opened under the grid (another one is put away first, with its creak; the one already
    /// shown just stays) and opens the screen.
    /// </summary>
    private void OpenChest(TileObjectInstance obj)
    {
        if (_panel != _chest || _chest.Container != obj)
        {
            HidePanel();
            _chest.Open(obj);
            ShowPanel(_chest, UiPanel.Chest);
        }
        SetOpen(true);
    }

    /// <summary>Puts <paramref name="stack"/> back into the player's storage; what does not fit is thrown.</summary>
    private void Stow(ItemStack stack)
    {
        if (stack.IsEmpty) return;
        _ui.Game.ThrowStack(_ui.Entity, _ui.Inventory.Pickup(stack));
    }
}
