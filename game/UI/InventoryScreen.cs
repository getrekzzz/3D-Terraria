using System;
using Godot;
using Strata.Core.Content.Defs;
using Strata.Core.Gameplay.Items;
using Strata.Core.Voxels.TileObjects;
using Strata.Game.Items;

namespace Strata.Game.UI;

/// <summary>
/// The inventory screen (Tab/I): backpack, equipment, trash and coins, the crafting list for the current
/// environment and an open container. Items move with a cursor stack (left/right/shift click).
/// </summary>
public sealed partial class InventoryScreen : CanvasLayer
{
    private const string Gate = "inventory";
    private const float SlotSize = 50;
    private UiContext _ui = null!;
    private Control _root = null!;
    private PanelContainer _chestPanel = null!;
    private GridContainer _chestGrid = null!;
    private Label _chestTitle = null!;
    private CraftingPanel _crafting = null!;
    private Label _coins = null!;
    private Label _defense = null!;
    private TextureRect _cursorIcon = null!;
    private Label _cursorCount = null!;
    private readonly Inventory _trash = new(1);
    private TileObjectInstance? _container;
    private Inventory? _containerInv;

    public bool IsOpen => _root.Visible;

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
        for (int i = 0; i < PlayerInventory.BackpackSize; i++) grid.AddChild(Slot(inv.Backpack, i, null, i < PlayerInventory.HotbarSize ? ((i + 1) % 10).ToString() : null));
        backpack.AddChild(grid);

        var equipment = Panel(new Vector2(24 + 10 * 54 + 40, 24));
        var eq = new VBoxContainer();
        eq.AddChild(new Label { Text = "Equipment" });
        var slots = new HBoxContainer();
        var armor = new VBoxContainer { TooltipText = "Armor: head, body, legs" };
        var kinds = new[] { EquipSlot.Head, EquipSlot.Body, EquipSlot.Legs };
        for (int i = 0; i < PlayerInventory.ArmorSlots; i++) armor.AddChild(Slot(inv.Armor, i, SlotActions.ArmorFilter(kinds[i]), null));
        var accessories = new VBoxContainer { TooltipText = "Accessories" };
        for (int i = 0; i < PlayerInventory.AccessorySlots; i++) accessories.AddChild(Slot(inv.Accessories, i, SlotActions.AccessoryFilter(inv.Accessories, i), null));
        slots.AddChild(armor);
        slots.AddChild(accessories);
        eq.AddChild(slots);
        _defense = new Label();
        eq.AddChild(_defense);
        eq.AddChild(new Label { Text = "Trash" });
        eq.AddChild(Slot(_trash, 0, null, null));
        _coins = new Label();
        eq.AddChild(_coins);
        equipment.AddChild(eq);

        _chestPanel = Panel(new Vector2(24, 24 + 5 * 54 + 40));
        var chestBox = new VBoxContainer();
        var header = new HBoxContainer();
        _chestTitle = new Label { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        header.AddChild(_chestTitle);
        header.AddChild(Button("Loot all", LootAll));
        header.AddChild(Button("Deposit all", DepositAll));
        chestBox.AddChild(header);
        _chestGrid = new GridContainer { Columns = 10 };
        _chestGrid.AddThemeConstantOverride("h_separation", 4);
        _chestGrid.AddThemeConstantOverride("v_separation", 4);
        chestBox.AddChild(_chestGrid);
        _chestPanel.AddChild(chestBox);
        _chestPanel.Visible = false;

        _crafting = new CraftingPanel();
        _root.AddChild(_crafting);
        _crafting.Initialize(ui, new Vector2(24 + 10 * 54 + 40 + 180, 96));

        _cursorIcon = new TextureRect { MouseFilter = Control.MouseFilterEnum.Ignore, Size = new Vector2(44, 44), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, ZIndex = 10 };
        _cursorCount = new Label { MouseFilter = Control.MouseFilterEnum.Ignore, ZIndex = 11 };
        _root.AddChild(_cursorIcon);
        _root.AddChild(_cursorCount);

        inv.Armor.Changed += _ => ui.Entity.State.Recompute();
        inv.Accessories.Changed += _ => ui.Entity.State.Recompute();
        ui.Game.Interaction.ContainerOpened += (p, obj) =>
        {
            if (p == ui.Entity) OpenContainer(obj);
        };
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (InputGate.TextEntry) return;
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
        InputGate.SetOpen(Gate, open);
        // Auto-pause (single player option): the world waits while the inventory is open.
        if (Settings.UserSettings.Current.AutoPause || !open) _ui.Player.Runtime.Simulating = !open;
        if (open)
        {
            _crafting.Refresh();
            return;
        }
        var cursor = _ui.Inventory.Cursor;
        var rest = SlotActions.Stow(ref cursor, _ui.Inventory.Backpack);
        _ui.Inventory.Cursor = cursor;
        if (!rest.IsEmpty) _ui.Game.Drops.Spawn(rest, _ui.Entity.Eye, System.Numerics.Vector3.Zero, pickupDelay: 120);
        CloseContainer();
    }

    public override void _Process(double delta)
    {
        if (!IsOpen) return;
        var state = _ui.Entity.State;
        _defense.Text = $"Defense {state.Defense}";
        _coins.Text = Coins.Format(Coins.Total(state.Inventory.Backpack));
        var cursor = _ui.Inventory.Cursor;
        _cursorIcon.Visible = !cursor.IsEmpty;
        _cursorCount.Visible = cursor.Count > 1;
        if (!cursor.IsEmpty)
        {
            var mouse = _root.GetLocalMousePosition();
            _cursorIcon.Texture = ItemIcons.Get(cursor.Item!);
            _cursorIcon.Position = mouse + new Vector2(6, 6);
            _cursorCount.Text = cursor.Count.ToString();
            _cursorCount.Position = mouse + new Vector2(8, 30);
        }
        if (_container != null)
        {
            var world = _ui.Game.Session.World;
            var center = new System.Numerics.Vector3(_container.Anchor.X + 1, _container.Anchor.Y + 1, _container.Anchor.Z + 1) * Strata.Core.Common.Units.CellSize;
            if (world.TileObjects.Get(_container.Id) == null || System.Numerics.Vector3.Distance(center, _ui.Entity.Center) > 6f) CloseContainer();
        }
    }

    private void OpenContainer(TileObjectInstance obj)
    {
        CloseContainer();
        _container = obj;
        _containerInv = new Inventory(obj.Container!);
        _chestTitle.Text = obj.Def.DisplayName;
        for (int i = 0; i < obj.Container!.Length; i++) _chestGrid.AddChild(Slot(_containerInv, i, null, null, markChunk: true));
        _chestPanel.Visible = true;
        SetOpen(true);
    }

    private void CloseContainer()
    {
        _container = null;
        _containerInv = null;
        _chestPanel.Visible = false;
        foreach (var child in _chestGrid.GetChildren()) child.QueueFree();
    }

    private ItemSlotView Slot(Inventory inv, int index, Func<ItemDef, bool>? accepts, string? badge, bool markChunk = false)
    {
        var view = new ItemSlotView(() => inv[index], SlotSize, _ui.Tooltip) { Badge = badge };
        view.Clicked += (button, shift) =>
        {
            var cursor = _ui.Inventory.Cursor;
            if (shift && cursor.IsEmpty) QuickMove(inv, index);
            else if (button == MouseButton.Left) SlotActions.LeftClick(ref cursor, inv, index, accepts);
            else SlotActions.RightClick(ref cursor, inv, index, accepts);
            _ui.Inventory.Cursor = cursor;
            if (markChunk) MarkContainerModified();
            _crafting.Refresh();
        };
        return view;
    }

    private void QuickMove(Inventory from, int index)
    {
        var backpack = _ui.Inventory.Backpack;
        if (from == backpack)
        {
            if (_containerInv != null) SlotActions.QuickMove(backpack, index, _containerInv);
            else if (!_ui.Inventory.Equip(index)) SlotActions.QuickMove(backpack, index, _trash);
            MarkContainerModified();
        }
        else
        {
            SlotActions.QuickMove(from, index, backpack);
            MarkContainerModified();
        }
    }

    private void LootAll()
    {
        if (_containerInv == null) return;
        for (int i = 0; i < _containerInv.Size; i++) SlotActions.QuickMove(_containerInv, i, _ui.Inventory.Backpack);
        MarkContainerModified();
    }

    private void DepositAll()
    {
        if (_containerInv == null) return;
        var backpack = _ui.Inventory.Backpack;
        for (int i = PlayerInventory.HotbarSize; i < backpack.Size; i++)
            if (!backpack[i].IsEmpty && backpack[i].Item!.Category != ItemCategory.Coin) SlotActions.QuickMove(backpack, i, _containerInv);
        MarkContainerModified();
    }

    private void MarkContainerModified()
    {
        if (_container == null) return;
        var chunk = _ui.Game.Session.World.GetChunk(Strata.Core.Common.ChunkMath.CellToChunk(_container.Anchor));
        chunk?.MarkModified();
    }

    private PanelContainer Panel(Vector2 position)
    {
        var p = new PanelContainer { Position = position };
        _root.AddChild(p);
        return p;
    }

    private static Button Button(string text, Action onPressed)
    {
        var b = new Button { Text = text };
        b.Pressed += onPressed;
        return b;
    }
}
