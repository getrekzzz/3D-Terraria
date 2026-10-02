using System;
using System.Collections.Generic;
using Godot;
using Strata.Core.Common;
using Strata.Core.Gameplay.Items;

namespace Strata.Game.UI;

/// <summary>The tooltip lines of a stack as a screen shows it (<see cref="UiContext.Tooltip(ItemStack)"/>, or a shop's buy price).</summary>
public delegate List<(string Text, TooltipKind Kind)> TooltipSource(ItemStack stack);

/// <summary>
/// One item slot, drawn the reference game's way (ItemSlotView.Draw.cs): tinted by <see cref="SlotKind"/>, gold when
/// it is the selected hotbar slot or favourited, the icon centred with a bold outlined stack count, the rounds left
/// on bows and guns, a faint silhouette when empty. Reads its stack through a delegate so it always shows the live
/// inventory; hovering shows the instant <see cref="TooltipFollower"/> and a held right button repeats the click at
/// <see cref="HoldRepeat"/>'s quickening pace. Hidden slots stop processing.
/// </summary>
public sealed partial class ItemSlotView : Control
{
    private static ulong _countedFrame;
    private static int _counting;

    private readonly Func<ItemStack> _read;
    private readonly TooltipSource? _tooltip;
    private SlotKind _kind;
    private bool _selected;
    private bool _locked;
    private bool _dimmed;
    private bool _short;
    private bool _hovered;
    private ItemStack _shown;
    private bool _favoriteShown;
    private int _ammoShown = -1;
    private bool _repeating;
    private int _repeats;
    private double _repeatWait;

    public ItemSlotView(Func<ItemStack> read, float size, SlotKind kind, TooltipSource? tooltip = null)
    {
        _read = read;
        _kind = kind;
        _tooltip = tooltip;
        CustomMinimumSize = new Vector2(size, size);
        MouseFilter = MouseFilterEnum.Stop;
        // Icons are drawn at about a third of their size; mipmaps (where imported) keep them from aliasing.
        TextureFilter = TextureFilterEnum.LinearWithMipmaps;
        MouseEntered += () =>
        {
            _hovered = true;
            ShowTooltip();
            QueueRedraw();
        };
        MouseExited += () =>
        {
            _hovered = false;
            StopRepeat();
            TooltipFollower.Current?.Hide(this);
            QueueRedraw();
        };
        VisibilityChanged += OnVisibilityChanged;
    }

    /// <summary>Slot views that processed in the last full frame (the <c>ui.slot_views</c> probe): hidden ones do not.</summary>
    public static int ActiveViews { get; private set; }

    /// <summary>The slot's tint (inventory blue, chest teal, equipment violet…).</summary>
    public SlotKind Kind
    {
        get => _kind;
        set => Redraw(ref _kind, value);
    }

    /// <summary>The selected hotbar slot: drawn gold.</summary>
    public bool Selected
    {
        get => _selected;
        set => Redraw(ref _selected, value);
    }

    /// <summary>A slot the world has not unlocked (Expert accessory slots): drawn dark, its item still shown.</summary>
    public bool Locked
    {
        get => _locked;
        set => Redraw(ref _locked, value);
    }

    /// <summary>The icon drawn greyed, the slot as usual (a recipe the player cannot craft yet).</summary>
    public bool Dimmed
    {
        get => _dimmed;
        set => Redraw(ref _dimmed, value);
    }

    /// <summary>A recipe material the player has too few of: its count is drawn red, even a count of one.</summary>
    public bool Short
    {
        get => _short;
        set => Redraw(ref _short, value);
    }

    /// <summary>Short label drawn in the top-left corner (hotbar numbers).</summary>
    public string? Badge { get; set; }

    /// <summary>Silhouette shown faintly while the slot is empty (armor, accessory and misc slots, trash).</summary>
    public Texture2D? EmptyHint { get; set; }

    /// <summary>The slot's name, shown as its tooltip while it is empty ('Helmet', 'Social Shirt', 'Dye'); null shows none.</summary>
    public string? EmptyText { get; set; }

    /// <summary>Whether the slot is favourited (Alt+click): drawn gold; null for slots that cannot be.</summary>
    public Func<bool>? Favorite { get; set; }

    /// <summary>Rounds of an ammo class carried (<see cref="PlayerInventory.CountAmmo"/>), shown on bows and guns; null to hide.</summary>
    public Func<string, int>? AmmoCount { get; set; }

    /// <summary>A held right button repeats the click (inventory and chest slots, not the hotbar).</summary>
    public bool RepeatRight { get; set; }

    /// <summary>True while <see cref="Clicked"/> runs for a hold-repeat rather than a press.</summary>
    public bool Repeating { get; private set; }

    /// <summary>(button, modifier keys) on click, and again for each hold-repeat of the right button.</summary>
    public event Action<MouseButton, ClickMods>? Clicked;

    /// <summary>Whether the mouse is over the slot.</summary>
    public bool Hovered => _hovered;

    /// <summary>
    /// Sizes the slot to <paramref name="side"/> pixels square, smaller than it was made too: the minimum size first, as
    /// a control never shrinks below its minimum (setting the size first would leave a shrinking slot at its old size).
    /// </summary>
    public void Resize(float side)
    {
        var size = new Vector2(side, side);
        CustomMinimumSize = size;
        Size = size;
    }

    /// <summary>Shows this slot's tooltip beside the pointer (on hover, and from the <c>ui hover</c> command).</summary>
    public void ShowTooltip()
    {
        if (_tooltip != null) TooltipFollower.Current?.Show(this, _read, _tooltip, EmptyText);
    }

    /// <summary>Ends a hold-repeat (the owner calls it when a repeated click stops moving items).</summary>
    public void StopRepeat() => _repeating = false;

    public override void _Process(double delta)
    {
        if (!IsVisibleInTree())
        {
            SetProcess(false); // until it is shown again
            return;
        }
        using var profile = Strata.Core.Diagnostics.Metrics.Frame.Time("gd.slot_views");
        Count();
        var s = _read();
        bool favorite = Favorite != null && !s.IsEmpty && Favorite();
        int ammo = AmmoCount != null && s.Item?.UseAmmo is { } ammoClass ? AmmoCount(ammoClass) : -1;
        if (!s.Equals(_shown) || favorite != _favoriteShown || ammo != _ammoShown)
        {
            _shown = s;
            _favoriteShown = favorite;
            _ammoShown = ammo;
            QueueRedraw();
        }
        if (_repeating) Repeat(delta);
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is not InputEventMouseButton { Pressed: true } mb || (mb.ButtonIndex != MouseButton.Left && mb.ButtonIndex != MouseButton.Right)) return;
        var mods = (mb.ShiftPressed ? ClickMods.Shift : 0) | (mb.CtrlPressed || mb.MetaPressed ? ClickMods.Ctrl : 0) | (mb.AltPressed ? ClickMods.Alt : 0);
        AcceptEvent();
        _repeating = RepeatRight && mb.ButtonIndex == MouseButton.Right && mods == ClickMods.None;
        _repeats = 0;
        _repeatWait = HoldRepeat.IntervalTicks(0) / (double)Units.TicksPerSecond;
        Clicked?.Invoke(mb.ButtonIndex, mods);
    }

    /// <summary>
    /// Shown: process again. Hidden (the inventory closing under the pointer sends no exit event): drop the hover
    /// state, the hold-repeat and the tooltip.
    /// </summary>
    private void OnVisibilityChanged()
    {
        bool visible = IsVisibleInTree();
        SetProcess(visible);
        if (visible) return;
        _hovered = false;
        StopRepeat();
        TooltipFollower.Current?.Hide(this);
    }

    /// <summary>Holding the right button keeps clicking, faster and faster, until it is released or the pointer leaves.</summary>
    private void Repeat(double delta)
    {
        if (!_hovered || !Input.IsMouseButtonPressed(MouseButton.Right))
        {
            _repeating = false;
            return;
        }
        _repeatWait -= delta;
        while (_repeatWait <= 0 && _repeating)
        {
            _repeatWait += HoldRepeat.IntervalTicks(++_repeats) / (double)Units.TicksPerSecond;
            Repeating = true;
            try
            {
                Clicked?.Invoke(MouseButton.Right, ClickMods.None);
            }
            finally
            {
                Repeating = false;
            }
        }
    }

    private static void Count()
    {
        ulong frame = Engine.GetProcessFrames();
        if (frame != _countedFrame)
        {
            ActiveViews = _counting;
            _counting = 0;
            _countedFrame = frame;
        }
        _counting++;
    }

    private void Redraw<T>(ref T field, T value)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        QueueRedraw();
    }
}
