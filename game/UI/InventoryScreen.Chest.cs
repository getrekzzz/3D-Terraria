using System.Collections.Generic;
using Godot;
using Strata.Core.Gameplay.Items;
using Strata.Core.Presentation;
using Strata.Core.Session;
using Strata.Core.Voxels.TileObjects;

namespace Strata.Game.UI;

/// <summary>
/// The open chest, shown in the panel host under the grid as Terraria shows it: its name over 40 slots, and right of
/// them the Loot All, Deposit All, Quick Stack, Restock, Sort Items and Rename links, all through
/// <see cref="GameWorld.ChestAction"/> and <see cref="GameWorld.RenameContainer"/> (the name is saved with the chunk).
/// The slot views are made once and read whichever chest is open, so opening one builds nothing.
/// </summary>
public sealed partial class InventoryScreen
{
    private const int ChestColumns = 10;
    private const int PooledChestSlots = 40;

    private readonly List<ItemSlotView> _chestViews = new(PooledChestSlots);
    private readonly List<Button> _chestButtons = new();
    private Control _chestPanel = null!;
    private OutlinedText _chestTitle = null!;
    private LineEdit _renameField = null!;
    private UiLayoutResult? _layout;

    private void BuildChestPanel()
    {
        _chestPanel = new Control { Name = "ChestPanel", Visible = false, MouseFilter = Control.MouseFilterEnum.Stop };
        _chestTitle = new OutlinedText { Align = HorizontalAlignment.Left };
        _chestPanel.AddChild(_chestTitle);
        _renameField = new LineEdit { MaxLength = TileObjectInstance.MaxNameLength, Visible = false };
        _renameField.AddThemeFontSizeOverride("font_size", UiFonts.Small);
        // Without the theme's 4 px padding above and below the text, so the field (as tall as the font's line) grows
        // less far past the 18 px caption row over the first row of chest slots while a name is typed.
        foreach (string box in new[] { "normal", "focus" })
        {
            var style = (StyleBox)UiTheme.Theme.GetStylebox(box, "LineEdit").Duplicate();
            style.ContentMarginTop = style.ContentMarginBottom = 0;
            _renameField.AddThemeStyleboxOverride(box, style);
        }
        _renameField.FocusEntered += () => InputGate.TextEntry = true;
        _renameField.FocusExited += () =>
        {
            InputGate.TextEntry = false;
            FinishRename();
        };
        _renameField.TextSubmitted += _ => _renameField.ReleaseFocus();
        _chestPanel.AddChild(_renameField);
        EnsureChestViews(PooledChestSlots);
        AddChestButton("Loot All", () => Chest(ChestOp.LootAll), "Take everything into the backpack");
        AddChestButton("Deposit All", () => Chest(ChestOp.DepositAll), "Everything below the hotbar except favourites and coins");
        AddChestButton("Quick Stack", () => Chest(ChestOp.QuickStack), "Only items this chest already holds");
        AddChestButton("Restock", () => Chest(ChestOp.Restock), "Top up your stacks from this chest");
        AddChestButton("Sort Items", () => Chest(ChestOp.Sort));
        AddChestButton("Rename", StartRename, $"Give this chest a name (up to {TileObjectInstance.MaxNameLength} characters)");
    }

    private void AddChestButton(string text, System.Action pressed, string? tooltip = null)
    {
        var b = TextButton(text, pressed, tooltip);
        _chestPanel.AddChild(b);
        _chestButtons.Add(b);
    }

    /// <summary>Slot views for a container of <paramref name="count"/> slots (every chest has 40; a bigger one adds views once).</summary>
    private void EnsureChestViews(int count)
    {
        while (_chestViews.Count < count)
        {
            var view = Slot(SlotArea.Container, _chestViews.Count);
            _chestPanel.AddChild(view);
            _chestViews.Add(view);
        }
    }

    /// <summary>Places the host on the last layout's Panel region (hidden without a panel or a region) and the chest in it.</summary>
    private void PlacePanel()
    {
        _panelHost.Visible = _panel != null && _layout != null && LayoutWidgets.Place(_panelHost, _layout, UiRegion.Panel);
        if (_panelHost.Visible && _panel == _chestPanel) ArrangeChest();
    }

    /// <summary>The chest's name on the caption row, its slots in rows of ten and the links in a column on the right.</summary>
    private void ArrangeChest()
    {
        var size = _panelHost.Size;
        float side = UiLayout.PanelSlot * SlotScale, pitch = side + UiLayout.Gap, top = UiLayout.Caption;
        float gridW = ChestColumns * pitch - UiLayout.Gap;
        int slots = _containerInv?.Size ?? 0;
        for (int i = 0; i < _chestViews.Count; i++)
        {
            var view = _chestViews[i];
            view.Visible = i < slots;
            if (view.Visible) PutSlot(view, i % ChestColumns * pitch, top + i / ChestColumns * pitch, side);
        }
        _chestTitle.Position = Vector2.Zero;
        _chestTitle.Size = new Vector2(gridW, top);
        _renameField.Position = Vector2.Zero;
        _renameField.Size = new Vector2(gridW, top);
        float x = gridW + 2 * UiLayout.Gap, row = (size.Y - top) / _chestButtons.Count;
        for (int i = 0; i < _chestButtons.Count; i++)
        {
            _chestButtons[i].Position = new Vector2(x, top + i * row);
            _chestButtons[i].Size = new Vector2(size.X - x, row);
        }
    }

    private void OpenContainer(TileObjectInstance obj)
    {
        HidePanel(); // another chest (or this one again) is put away first
        _container = obj;
        _containerInv = new Inventory(obj.Container!);
        ContainerShown?.Invoke(obj, true);
        _chestTitle.Text = obj.DisplayName;
        EnsureChestViews(obj.Container!.Length);
        ShowPanel(_chestPanel, UiPanel.Chest);
        SetOpen(true);
    }

    /// <summary>The chest is no longer shown: typing its name ends and its lid closes.</summary>
    private void ReleaseContainer()
    {
        if (_renameField.Visible) _renameField.ReleaseFocus();
        if (_container != null) ContainerShown?.Invoke(_container, false);
        _container = null;
        _containerInv = null;
    }

    private void Chest(ChestOp op)
    {
        if (_container == null) return;
        _ui.Game.ChestAction(_ui.Entity, _container, op);
        Audio.Sfx.Play("grab");
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
}
