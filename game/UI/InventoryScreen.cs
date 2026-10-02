using System;
using Godot;
using Strata.Core.Gameplay.Items;
using Strata.Core.Presentation;
using Strata.Core.Voxels.TileObjects;

namespace Strata.Game.UI;

/// <summary>
/// The inventory screen (Tab/I), laid out like Terraria's on the HUD's <see cref="UiLayout"/>: the 10×5 backpack on
/// the hotbar's spot (its first row is the hotbar), the coin and ammo columns right of it with the trash under them and
/// the Sort Inventory and Quick Stack to Nearby Chests buttons beside them, the panel host under the grid (a chest, a
/// resident's shop or their service: the reforge slot), the crafting column bottom-left and the
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

    /// <summary>The screen is open with auto-pause on, so the world waits until it closes.</summary>
    public bool PausesWorld { get; private set; }

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
        // Talking to a resident puts an open shop or service away (the chat box takes over; it runs before the box opens).
        ui.Interaction.TalkRequested += _ =>
        {
            if (_panel == _shop || _panel == _service) ClosePanel();
        };
    }

    public override void _UnhandledInput(InputEvent e)
    {
        // Clicks on empty screen (throwing the held stack, using the item, turning the camera) go to WorldClickCatcher.
        if (InputGate.TextEntry || (!IsOpen && InputGate.GameplayBlocked) || InputGate.IsOpen(PauseMenu.Gate)) return;
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
        PausesWorld = open && autoPause;
        if (open)
        {
            _equipment.Refresh(_ui.Entity.State);
            _crafting.Refresh();
            LayoutChanged?.Invoke();
            return;
        }
        // The held stack goes back into the storage (coins and ammo to their slots; what does not fit is thrown), and
        // the service slot's item too (the reforge panel puts it away as it closes; what does not fit drops at the feet).
        var inv = _ui.Inventory;
        var held = inv.Cursor;
        inv.Cursor = ItemStack.Empty;
        Stow(held);
        HidePanel();
        ServicePanel.StowSlot(_ui);
        LayoutChanged?.Invoke();
    }

    public override void _Process(double delta)
    {
        using var profile = Strata.Core.Diagnostics.Metrics.Frame.Time("gd.inventory_ui");
        if (!IsOpen) return;
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

    /// <summary>
    /// Shows <paramref name="npc"/>'s shop under the grid where a chest would be (the chat box's Shop button) and opens
    /// the screen; the one already shown just stays.
    /// </summary>
    public void OpenShop(Strata.Core.Gameplay.Town.TownNpc npc)
    {
        if (_panel != _shop || _shop.Npc != npc)
        {
            HidePanel();
            _shop.Open(npc);
            ShowPanel(_shop, UiPanel.Shop);
        }
        SetOpen(true);
    }

    /// <summary>
    /// Shows <paramref name="npc"/>'s service under the grid in <paramref name="mode"/> (the chat box's Reforge) and opens
    /// the screen; the one already shown just stays.
    /// </summary>
    public void OpenService(Strata.Core.Gameplay.Town.TownNpc npc, ServiceMode mode)
    {
        if (_panel != _service || _service.Npc != npc || _service.Mode != mode)
        {
            HidePanel();
            _service.Open(npc, mode);
            ShowPanel(_service, UiPanel.Service);
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
