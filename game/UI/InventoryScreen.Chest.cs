using Godot;
using Strata.Core.Gameplay.Items;
using Strata.Core.Session;
using Strata.Core.Voxels.TileObjects;

namespace Strata.Game.UI;

/// <summary>
/// The open chest: its slots, the reference game's buttons (Loot all, Deposit all, Quick stack, Restock, Sort) and
/// renaming (saved with the chunk), all through <see cref="GameWorld.ChestAction"/> and <see cref="GameWorld.RenameContainer"/>;
/// plus the inventory's own Sort and Quick stack to nearby chests.
/// </summary>
public sealed partial class InventoryScreen
{
    private PanelContainer _chestPanel = null!;
    private GridContainer _chestGrid = null!;
    private Label _chestTitle = null!;
    private LineEdit _renameField = null!;

    private void BuildChestPanel(Vector2 position)
    {
        _chestPanel = Panel(position);
        var chestBox = new VBoxContainer();
        var header = new HBoxContainer();
        _chestTitle = new Label { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _renameField = new LineEdit { MaxLength = TileObjectInstance.MaxNameLength, Visible = false, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _renameField.FocusEntered += () => InputGate.TextEntry = true;
        _renameField.FocusExited += () =>
        {
            InputGate.TextEntry = false;
            FinishRename();
        };
        _renameField.TextSubmitted += _ => _renameField.ReleaseFocus();
        header.AddChild(_chestTitle);
        header.AddChild(_renameField);
        header.AddChild(Button("Rename", StartRename, "Give this chest a name (up to 20 characters)"));
        chestBox.AddChild(header);
        var buttons = new HBoxContainer();
        buttons.AddChild(Button("Loot all", () => Chest(ChestOp.LootAll)));
        buttons.AddChild(Button("Deposit all", () => Chest(ChestOp.DepositAll), "Everything below the hotbar except favourites and coins"));
        buttons.AddChild(Button("Quick stack", () => Chest(ChestOp.QuickStack), "Only items this chest already holds"));
        buttons.AddChild(Button("Restock", () => Chest(ChestOp.Restock), "Top up your stacks from this chest"));
        buttons.AddChild(Button("Sort", () => Chest(ChestOp.Sort)));
        chestBox.AddChild(buttons);
        _chestGrid = new GridContainer { Columns = 10 };
        _chestGrid.AddThemeConstantOverride("h_separation", 4);
        _chestGrid.AddThemeConstantOverride("v_separation", 4);
        chestBox.AddChild(_chestGrid);
        _chestPanel.AddChild(chestBox);
        _chestPanel.Visible = false;
    }

    private void OpenContainer(TileObjectInstance obj)
    {
        CloseContainer();
        _container = obj;
        _containerInv = new Inventory(obj.Container!);
        _chestTitle.Text = obj.DisplayName;
        for (int i = 0; i < obj.Container!.Length; i++) _chestGrid.AddChild(Slot(SlotArea.Container, i));
        _chestPanel.Visible = true;
        SetOpen(true);
    }

    private void CloseContainer()
    {
        if (_renameField.Visible) _renameField.ReleaseFocus();
        _container = null;
        _containerInv = null;
        _chestPanel.Visible = false;
        foreach (var child in _chestGrid.GetChildren()) child.QueueFree();
    }

    private void Chest(ChestOp op)
    {
        if (_container == null) return;
        _ui.Game.ChestAction(_ui.Entity, _container, op);
        _crafting.Refresh();
    }

    private void StartRename()
    {
        if (_container == null) return;
        _renameField.Text = _container.CustomName ?? "";
        _renameField.PlaceholderText = _container.Def.DisplayName;
        _chestTitle.Visible = false;
        _renameField.Visible = true;
        _renameField.GrabFocus();
        _renameField.SelectAll();
    }

    private void FinishRename()
    {
        if (!_renameField.Visible) return;
        _renameField.Visible = false;
        _chestTitle.Visible = true;
        if (_container == null) return;
        _ui.Game.RenameContainer(_container, _renameField.Text);
        _chestTitle.Text = _container.DisplayName;
    }

    private void SortBackpack()
    {
        InventoryOps.Sort(_ui.Inventory.Backpack, PlayerInventory.HotbarSize, PlayerInventory.BackpackSize);
        _crafting.Refresh();
    }

    private void QuickStackNearby()
    {
        if (_ui.Game.QuickStackNearby(_ui.Entity).Count > 0) _crafting.Refresh();
    }
}
