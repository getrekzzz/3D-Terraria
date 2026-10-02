using System;
using Godot;
using Strata.Core.Gameplay.Items;
using Strata.Core.Voxels.TileObjects;

namespace Strata.Game.UI;

/// <summary>
/// The inventory screen (Tab/I): backpack, equipment (armor, the accessory slots the world unlocks and the misc row:
/// pet, light pet, minecart, mount, hook), trash and coins, the crafting list for the current environment and an open
/// container. Items move with a cursor stack (drawn at the pointer by <see cref="CursorItemLayer"/>); clicks are routed by
/// <c>GameWorld.ClickSlot</c> (InventoryScreen.Slots.cs).
/// </summary>
public sealed partial class InventoryScreen : CanvasLayer
{
    private const string Gate = "inventory";
    private const float SlotSize = 50;
    private UiContext _ui = null!;
    private Control _root = null!;
    private CraftingPanel _crafting = null!;
    private Label _coins = null!;
    private Label _defense = null!;
    private Label _setBonus = null!;
    private Button _vanityToggle = null!;
    private readonly ItemSlotView[] _accessoryViews = new ItemSlotView[PlayerInventory.MaxAccessorySlots];
    private readonly ItemSlotView[] _hotbarViews = new ItemSlotView[PlayerInventory.HotbarSize];
    private TileObjectInstance? _container;
    private Inventory? _containerInv;

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

        var inv = ui.Inventory;
        var backpack = Panel(new Vector2(24, 24));
        var grid = new GridContainer { Columns = 10 };
        grid.AddThemeConstantOverride("h_separation", 4);
        grid.AddThemeConstantOverride("v_separation", 4);
        for (int i = 0; i < PlayerInventory.BackpackSize; i++)
        {
            var view = Slot(SlotArea.Backpack, i, i < PlayerInventory.HotbarSize ? ((i + 1) % 10).ToString() : null);
            if (i < PlayerInventory.HotbarSize) _hotbarViews[i] = view;
            grid.AddChild(view);
        }
        backpack.AddChild(grid);

        var equipment = Panel(new Vector2(24 + 10 * 54 + 40, 24));
        var eq = new VBoxContainer();
        var title = new HBoxContainer();
        title.AddChild(new Label { Text = "Equipment", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        title.AddChild(_vanityToggle = Button("Vanity", ToggleVanity));
        eq.AddChild(title);
        var slots = new HBoxContainer();
        slots.AddThemeConstantOverride("separation", 4); // three columns still fit left of the crafting list
        var armor = new VBoxContainer { TooltipText = "Armor: head, body, legs" };
        for (int i = 0; i < PlayerInventory.ArmorSlots; i++) armor.AddChild(WornSlot(SlotArea.Armor, SlotArea.VanityArmor, i));
        var accessories = new VBoxContainer { TooltipText = "Accessories" };
        for (int i = 0; i < PlayerInventory.MaxAccessorySlots; i++)
            accessories.AddChild(_accessoryViews[i] = WornSlot(SlotArea.Accessories, SlotArea.VanityAccessories, i));
        var misc = new VBoxContainer { TooltipText = "Pet, light pet, minecart, mount, hook" };
        for (int i = 0; i < MiscSlots.Count; i++) misc.AddChild(Slot(SlotArea.Misc, i));
        slots.AddChild(armor);
        slots.AddChild(accessories);
        slots.AddChild(misc);
        eq.AddChild(slots);
        _defense = new Label();
        eq.AddChild(_defense);
        _setBonus = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(160, 0) };
        _setBonus.AddThemeColorOverride("font_color", new Color(0.55f, 0.95f, 0.55f));
        _setBonus.AddThemeFontSizeOverride("font_size", 12);
        eq.AddChild(_setBonus);
        eq.AddChild(new Label { Text = "Trash" });
        eq.AddChild(Slot(SlotArea.Trash, 0));
        _coins = new Label();
        eq.AddChild(_coins);
        var tidy = new HBoxContainer();
        tidy.AddChild(Button("Sort", SortBackpack, "Sort the backpack below the hotbar (favourites stay put)"));
        tidy.AddChild(Button("Quick stack", QuickStackNearby, "Quick stack to nearby chests: items they already hold leave the backpack"));
        eq.AddChild(tidy);
        equipment.AddChild(eq);

        BuildChestPanel(new Vector2(24, 24 + 5 * 54 + 40));

        _crafting = new CraftingPanel();
        _root.AddChild(_crafting);
        _crafting.Initialize(ui, new Vector2(24 + 10 * 54 + 40 + 180, 96));

        ui.Game.Interaction.ContainerOpened += (p, obj) =>
        {
            if (p == ui.Entity) OpenContainer(obj);
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
            _crafting.Refresh();
            return;
        }
        // The held stack goes back into the storage (coins and ammo to their slots); what does not fit is thrown.
        var rest = _ui.Inventory.Pickup(_ui.Inventory.Cursor);
        _ui.Inventory.Cursor = ItemStack.Empty;
        _ui.Game.ThrowStack(_ui.Entity, rest);
        _craftingStale = false;
        CloseContainer();
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
        var state = _ui.Entity.State;
        _defense.Text = $"Defense {state.Defense}";
        _setBonus.Text = state.SetBonus != null ? $"Set bonus: {state.SetBonus}" : "";
        _coins.Text = Coins.Format(Coins.Total(state.Inventory));
        // Expert and Master worlds unlock the 6th and 7th accessory slots; a locked slot still shows an item left in it.
        var row = _showVanity ? state.Inventory.VanityAccessories : state.Inventory.Accessories;
        for (int i = 0; i < _accessoryViews.Length; i++)
        {
            bool unlocked = i < state.Inventory.AccessorySlotsUnlocked;
            _accessoryViews[i].Visible = unlocked || !row[i].IsEmpty;
            _accessoryViews[i].Locked = !unlocked;
        }
        for (int i = 0; i < _hotbarViews.Length; i++) _hotbarViews[i].Selected = state.Inventory.SelectedHotbar == i;
        if (_container != null)
        {
            var world = _ui.Game.Session.World;
            var center = new System.Numerics.Vector3(_container.Anchor.X + 1, _container.Anchor.Y + 1, _container.Anchor.Z + 1) * Strata.Core.Common.Units.CellSize;
            if (world.TileObjects.Get(_container.Id) == null || System.Numerics.Vector3.Distance(center, _ui.Entity.Center) > 6f) CloseContainer();
        }
    }

    private PanelContainer Panel(Vector2 position)
    {
        var p = new PanelContainer { Position = position };
        _root.AddChild(p);
        return p;
    }

    private static Button Button(string text, Action onPressed, string? tooltip = null)
    {
        // No keyboard focus: the player walks and jumps with the inventory open, and Space (ui_accept) would press it again.
        var b = new Button { Text = text, TooltipText = tooltip ?? "", FocusMode = Control.FocusModeEnum.None };
        b.Pressed += onPressed;
        return b;
    }
}
