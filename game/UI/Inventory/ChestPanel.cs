using System;
using System.Collections.Generic;
using Godot;
using Strata.Core.Common;
using Strata.Core.Gameplay.Items;
using Strata.Core.Presentation;
using Strata.Core.Session;
using Strata.Core.Voxels.TileObjects;

namespace Strata.Game.UI;

/// <summary>
/// The open chest, shown in the inventory's panel host under the grid as Terraria shows it: its name (the custom one,
/// else the chest's) over its slots in teal rows of ten, and right of them Terraria's white text links that glow gold
/// under the mouse: Loot All, Deposit All, Quick Stack, Restock, Sort Items and Rename. The buttons call
/// <see cref="GameWorld.ChestAction"/> (favourites stay in the backpack; Core marks the chest's chunk for saving) and
/// <see cref="GameWorld.RenameContainer"/>. Renaming swaps the title for a field that holds the keyboard
/// (<see cref="InputGate.TextEntry"/> and a modal 'rename' gate, so typing never moves the player): Enter keeps the
/// name, Esc cancels. The panel owns opening and putting the chest away: the lid follows <see cref="Shown"/>, the lid
/// creaks shut (<c>chest_close</c>) and the panel closes when the player walks more than <see cref="Reach"/> away.
/// The slot views are made once and read whichever chest is open, so opening one builds nothing.
/// </summary>
public sealed partial class ChestPanel : Control
{
    /// <summary>Metres from the chest's centre before it is put away.</summary>
    public const float Reach = 6f;

    private const int Columns = 10;
    private const int PooledSlots = 40;
    private const string RenameGate = "rename";

    private readonly UiContext _ui;
    private readonly Func<SlotArea, int, ItemSlotView> _slot;
    private readonly List<ItemSlotView> _views = new(PooledSlots);
    private readonly List<Button> _buttons = new();
    private readonly OutlinedText _title;
    private readonly LineEdit _renameField;
    private bool _renameCancelled;

    /// <param name="slot">Makes the view of a slot (the screen routes its clicks to <c>GameWorld.ClickSlot</c>).</param>
    public ChestPanel(UiContext ui, Func<SlotArea, int, ItemSlotView> slot)
    {
        _ui = ui;
        _slot = slot;
        Name = "ChestPanel";
        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;
        AddChild(_title = new OutlinedText { Align = HorizontalAlignment.Left });
        AddChild(_renameField = RenameField());
        EnsureViews(PooledSlots);
        AddButton("Loot All", () => Press(ChestOp.LootAll), "Take everything into the backpack");
        AddButton("Deposit All", () => Press(ChestOp.DepositAll), "Everything below the hotbar except favourites and coins");
        AddButton("Quick Stack", () => Press(ChestOp.QuickStack), "Only items this chest already holds");
        AddButton("Restock", () => Press(ChestOp.Restock), "Top up your stacks from this chest");
        AddButton("Sort Items", () => Press(ChestOp.Sort));
        AddButton("Rename", StartRename, $"Give this chest a name (up to {TileObjectInstance.MaxNameLength} characters)");
    }

    /// <summary>The chest shown, if any.</summary>
    public TileObjectInstance? Container { get; private set; }

    /// <summary>The shown chest's slots (a view over its saved container).</summary>
    public Inventory? ContainerInventory { get; private set; }

    /// <summary>The title shown over the slots (the <c>ui.chest_title</c> probe reads it).</summary>
    public string Title => _title.Text;

    /// <summary>The name field is shown over the title (it holds the keyboard).</summary>
    public bool Renaming => _renameField.Visible;

    /// <summary>Chests put away with their closing creak this session (the <c>ui.chests_closed</c> probe).</summary>
    public int ClosedCount { get; private set; }

    /// <summary>A chest's contents were shown (true) or put away (false); its lid follows.</summary>
    public event Action<TileObjectInstance, bool>? Shown;

    /// <summary>A button moved items: the screen refreshes what depends on the backpack (the crafting list).</summary>
    public event Action? ItemsMoved;

    /// <summary>The chest went out of reach or was broken: the screen closes the panel.</summary>
    public event Action? CloseRequested;

    /// <summary>Shows <paramref name="obj"/>'s contents (the screen puts any other panel away first).</summary>
    public void Open(TileObjectInstance obj)
    {
        Container = obj;
        ContainerInventory = new Inventory(obj.Container!);
        _title.Text = obj.DisplayName;
        EnsureViews(obj.Container!.Length);
        Shown?.Invoke(obj, true);
    }

    /// <summary>The chest is no longer shown: typing its name ends (keeping it), its lid closes with a creak.</summary>
    public void Release()
    {
        if (Renaming) EndRename();
        var chest = Container;
        Container = null;
        ContainerInventory = null;
        if (chest == null) return;
        Shown?.Invoke(chest, false);
        ClosedCount++;
        Audio.Sfx.PlayAt("chest_close", Player.PlayerController.ToGodot(Centre(chest)));
    }

    public override void _Process(double delta)
    {
        // The chest closes when the player walks off (or it is broken), as Terraria's does.
        if (Container == null || !IsVisibleInTree()) return;
        bool gone = _ui.Game.Session.World.TileObjects.Get(Container.Id) == null;
        if (gone || System.Numerics.Vector3.Distance(Centre(Container), _ui.Entity.Center) > Reach)
        {
            CloseRequested?.Invoke();
            return;
        }
        // A name given another way (the chest command, another player) shows at once; DisplayName allocates nothing.
        if (!Renaming) _title.Text = Container.DisplayName;
    }

    /// <summary>The chest's name on the caption row, its slots in rows of ten and the links in a column on the right.</summary>
    public void Arrange(float slotScale)
    {
        float side = UiLayout.PanelSlot * slotScale, pitch = side + UiLayout.Gap, top = UiLayout.Caption;
        float gridW = Columns * pitch - UiLayout.Gap;
        int slots = ContainerInventory?.Size ?? 0;
        for (int i = 0; i < _views.Count; i++)
        {
            var view = _views[i];
            view.Visible = i < slots;
            if (!view.Visible) continue;
            view.Position = new Vector2(i % Columns * pitch, top + i / Columns * pitch);
            view.CustomMinimumSize = view.Size = new Vector2(side, side);
        }
        _title.Position = _renameField.Position = Vector2.Zero;
        _title.Size = _renameField.Size = new Vector2(gridW, top);
        float x = gridW + 2 * UiLayout.Gap, row = (Size.Y - top) / _buttons.Count;
        for (int i = 0; i < _buttons.Count; i++)
        {
            _buttons[i].Position = new Vector2(x, top + i * row);
            _buttons[i].Size = new Vector2(Size.X - x, row);
        }
    }

    /// <summary>Presses one of the chest's buttons (the panel's links and the <c>ui chest</c> command); returns the items moved.</summary>
    public int Press(ChestOp op)
    {
        if (Container == null) return 0;
        int moved = _ui.Game.ChestAction(_ui.Entity, Container, op);
        Audio.Sfx.Play("grab");
        ItemsMoved?.Invoke();
        return moved;
    }

    /// <summary>A bare text button in the small UI font, white turning gold under the mouse (Terraria's chest links).</summary>
    public static Button TextButton(string text, Action onPressed, string? tooltip = null)
    {
        // No keyboard focus: the player walks and jumps with the inventory open, and Space (ui_accept) would press it again.
        var b = new Button
        {
            Text = text, TooltipText = tooltip ?? "", FocusMode = FocusModeEnum.None, ThemeTypeVariation = UiTheme.TextButton,
            Alignment = HorizontalAlignment.Left, ClipText = true,
        };
        b.AddThemeFontSizeOverride("font_size", UiFonts.Small);
        b.Pressed += onPressed;
        return b;
    }

    private void AddButton(string text, Action pressed, string? tooltip = null)
    {
        var b = TextButton(text, pressed, tooltip);
        AddChild(b);
        _buttons.Add(b);
    }

    /// <summary>Slot views for a container of <paramref name="count"/> slots (every chest has 40; a bigger one adds views once).</summary>
    private void EnsureViews(int count)
    {
        while (_views.Count < count)
        {
            var view = _slot(SlotArea.Container, _views.Count);
            AddChild(view);
            _views.Add(view);
        }
    }

    private static System.Numerics.Vector3 Centre(TileObjectInstance chest) =>
        new System.Numerics.Vector3(chest.Anchor.X + 1, chest.Anchor.Y + 1, chest.Anchor.Z + 1) * Units.CellSize;
}
