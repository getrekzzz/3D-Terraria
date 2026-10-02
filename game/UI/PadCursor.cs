using System.Collections.Generic;
using Godot;
using Strata.Core.Gameplay.Items;
using Strata.Core.Presentation;

namespace Strata.Game.UI;

/// <summary>
/// The controller's cursor on the gameplay screens (the inventory with its chest, shop, reforge slot, crafting and
/// equipment columns, and the NPC chat box), as Terraria's gamepad inventory. While the controller is in use
/// (<see cref="InputDevice"/>) and one of them is open, the cursor sits on a slot or button, framed in gold, and the
/// controller works the screen instead of the world (<see cref="InputGate.PadNavigating"/>: the player stands still):
/// the D-pad or left stick steps it to the neighbouring slot or button in that direction across every panel
/// (<see cref="FocusNavigation"/>, repeating while held), A clicks, X takes one (a right click; held, it repeats),
/// RB quick-moves (Shift-click: to the open chest, a sale, the reforge slot), LB marks a favourite and B closes the
/// screen. Clicks go through the slot's own click (<see cref="ItemSlotView.Press"/>) or the button's press, so every
/// rule the mouse follows applies. The held stack and the tooltip follow the cursor (<see cref="Pointer"/>), and on a
/// desktop the system pointer is moved onto it too. Targets are gathered only when the cursor moves.
/// </summary>
public sealed partial class PadCursor : CanvasLayer
{
    public const string NodeName = "PadCursor";

    private static readonly StringName Up = "ui_up", Down = "ui_down", Left = "ui_left", Right = "ui_right";
    private static readonly StringName Split = "ui_split";

    /// <summary>The screen buttons in the order <see cref="Act"/> handles them.</summary>
    private static readonly StringName[] Buttons = { "ui_accept", "ui_cancel", Split, "ui_quick_move", "ui_favorite" };

    private static PadCursor? _current;

    private readonly List<Control> _targets = new();
    private readonly List<NavRect> _rects = new();
    private readonly bool[] _down = new bool[Buttons.Length];
    private UiContext _ui = null!;
    private InventoryScreen _inventory = null!;
    private TownDialog _dialog = null!;
    private Control? _chat;
    private PadFocusFrame _frame = null!;
    private Control? _focus;
    private NavDirection? _heldDirection;
    private double _repeatWait;

    /// <summary>Where the held stack and the tooltip follow while the controller drives a screen (null otherwise).</summary>
    public static Vector2? Pointer { get; private set; }

    /// <summary>The take-one button is held while the cursor drives a screen (a slot's hold-repeat).</summary>
    public static bool SplitHeld => _current is { Active: true } && Input.IsActionPressed(Split);

    /// <summary>The controller drives an open screen.</summary>
    public bool Active { get; private set; }

    /// <summary>The slot or button under the cursor.</summary>
    public Control? Focus => _focus;

    /// <summary>Slots and buttons the cursor could reach at its last step (the <c>pad.targets</c> probe).</summary>
    public int TargetCount => _targets.Count;

    /// <summary>Clicks made with the controller's buttons (the <c>pad.presses</c> probe).</summary>
    public int Presses { get; private set; }

    public void Initialize(UiContext ui, InventoryScreen inventory, TownDialog dialog)
    {
        _ui = ui;
        _inventory = inventory;
        _dialog = dialog;
        Name = NodeName;
        Layer = 88; // over the inventory and the chat box, under the held stack and the tooltip
        _chat = dialog.GetNodeOrNull<Control>("NpcChatRegion/NpcChat");
        AddChild(_frame = new PadFocusFrame());
        _current = this;
    }

    public override void _ExitTree()
    {
        if (_current != this) return;
        _current = null;
        Pointer = null;
        InputGate.PadNavigating = false;
    }

    public override void _Process(double delta)
    {
        bool open = _inventory.IsOpen || _dialog.IsOpen;
        bool active = open && InputDevice.UsingPad && !InputGate.ModalOtherThan(InventoryScreen.GateName) && !InputGate.TextEntry;
        if (active != Active)
        {
            Active = active;
            if (active) _heldDirection = HeldDirection(); // a stick already held (walking) does not step the cursor at once
            else Release();
        }
        InputGate.PadNavigating = active;
        if (!active)
        {
            PollButtons(false);
            return;
        }
        using var profile = Strata.Core.Diagnostics.Metrics.Frame.Time("gd.pad_cursor");
        if (!Usable(_focus)) SetFocus(DefaultFocus(), sound: false);
        if (Direction(delta) is { } dir) Step(dir);
        PollButtons(true);
        Track();
    }

    /// <summary>
    /// Moves the cursor one step (the D-pad, a stick tilt, or the <c>pad</c> command's presses); false when nothing
    /// lies that way.
    /// </summary>
    public bool Step(NavDirection dir)
    {
        Collect();
        if (_targets.Count == 0) return false;
        int from = _focus != null ? _targets.IndexOf(_focus) : -1;
        int next;
        if (from >= 0) next = FocusNavigation.Next(_rects, _rects[from], dir, from);
        else
        {
            var at = Pointer ?? GetViewport().GetVisibleRect().GetCenter();
            next = FocusNavigation.Nearest(_rects, at.X, at.Y);
        }
        if (next < 0) return false;
        SetFocus(_targets[next], sound: true);
        return true;
    }

    /// <summary>The strongest direction held on the D-pad or the left stick (the screen actions), or null.</summary>
    private static NavDirection? HeldDirection()
    {
        float x = Input.GetActionStrength(Right) - Input.GetActionStrength(Left);
        float y = Input.GetActionStrength(Down) - Input.GetActionStrength(Up);
        if (x == 0 && y == 0) return null;
        return Mathf.Abs(x) > Mathf.Abs(y) ? (x > 0 ? NavDirection.Right : NavDirection.Left) : (y > 0 ? NavDirection.Down : NavDirection.Up);
    }

    /// <summary>A new direction steps at once; held, it repeats after the delay at the interval (data/config/input.json, "pad").</summary>
    private NavDirection? Direction(double delta)
    {
        var held = HeldDirection();
        var pad = Bootstrap.InputSetup.Config.Pad;
        if (held != _heldDirection)
        {
            _heldDirection = held;
            _repeatWait = pad.RepeatDelay;
            return held;
        }
        if (held == null) return null;
        _repeatWait -= delta;
        if (_repeatWait > 0) return null;
        _repeatWait = Mathf.Max(_repeatWait + pad.RepeatInterval, 0);
        return held;
    }

    /// <summary>
    /// Each screen button acts once per press. Polled, so presses from the <c>pad</c> command and quick taps between two
    /// frames both count; states are tracked while inactive too, so a button already down never acts.
    /// </summary>
    private void PollButtons(bool live)
    {
        for (int i = 0; i < Buttons.Length; i++)
        {
            bool down = Input.IsActionPressed(Buttons[i]);
            bool fresh = (down && !_down[i]) || Input.IsActionJustPressed(Buttons[i]);
            _down[i] = down;
            if (live && fresh && Active) Act(i);
        }
    }

    private void Act(int button)
    {
        switch (button)
        {
            case 0: Press(MouseButton.Left, ClickMods.None); break;
            case 1: CloseTop(); break;
            case 2: Press(MouseButton.Right, ClickMods.None); break;
            case 3: Press(MouseButton.Left, ClickMods.Shift); break;
            default: Press(MouseButton.Left, ClickMods.Alt); break;
        }
    }

    /// <summary>A click on the slot or button under the cursor, as the mouse makes it.</summary>
    private void Press(MouseButton button, ClickMods mods)
    {
        if (!Usable(_focus)) return;
        switch (_focus)
        {
            case ItemSlotView slot:
                Presses++;
                slot.Press(button, mods);
                break;
            case BaseButton b when button == MouseButton.Left && mods == ClickMods.None:
                Presses++;
                if (!b.ToggleMode) b.EmitSignal(BaseButton.SignalName.Pressed);
                else if (b.ButtonGroup == null || !b.ButtonPressed) b.ButtonPressed = !b.ButtonPressed;
                break;
        }
    }

    /// <summary>B: the inventory closes first (it stows the held stack), then the chat box.</summary>
    private void CloseTop()
    {
        if (_inventory.IsOpen) _inventory.SetOpen(false);
        else _dialog.Close();
    }

    /// <summary>The selected hotbar slot when the inventory opens, else the first button of the chat box.</summary>
    private Control? DefaultFocus()
    {
        if (_inventory.IsOpen && _inventory.FindSlot(new SlotRef(SlotArea.Backpack, _ui.Inventory.SelectedHotbar)) is { } slot && Usable(slot))
            return slot;
        Collect();
        return _targets.Count > 0 ? _targets[0] : null;
    }

    /// <summary>Gathers the visible slots and buttons of the open screens with their screen rectangles.</summary>
    private void Collect()
    {
        _targets.Clear();
        _rects.Clear();
        if (_inventory.IsOpen) Walk(_inventory.PadRoot);
        if (_dialog.IsOpen && _chat != null && _chat.IsVisibleInTree()) Walk(_chat);
    }

    private void Walk(Control parent)
    {
        int count = parent.GetChildCount();
        for (int i = 0; i < count; i++)
        {
            if (parent.GetChild(i) is not Control { Visible: true } c) continue;
            if (c is ItemSlotView or BaseButton)
            {
                if (!Usable(c)) continue;
                var r = c.GetGlobalRect();
                if (r.Size.X < 2 || r.Size.Y < 2) continue;
                _targets.Add(c);
                _rects.Add(new NavRect(r.Position.X, r.Position.Y, r.Size.X, r.Size.Y));
                continue;
            }
            Walk(c);
        }
    }

    private static bool Usable(Control? c) =>
        c != null && IsInstanceValid(c) && c.IsVisibleInTree() && c switch
        {
            ItemSlotView slot => slot.Interactive,
            BaseButton b => !b.Disabled,
            _ => false,
        };

    private void SetFocus(Control? c, bool sound)
    {
        if (c == _focus) return;
        if (_focus is ItemSlotView old && IsInstanceValid(old))
        {
            old.PadFocused = false;
            TooltipFollower.Current?.Hide(old);
        }
        _focus = c;
        if (c == null) return;
        if (c is ItemSlotView slot)
        {
            slot.PadFocused = true;
            slot.ShowTooltip();
        }
        // A recipe scrolled out of the crafting column comes into view.
        for (var n = c.GetParent(); n != null; n = n.GetParent())
            if (n is ScrollContainer scroll)
            {
                scroll.EnsureControlVisible(c);
                break;
            }
        if (DisplayServer.GetName() != "headless")
        {
            // The system pointer goes along, so hover effects and a later mouse movement start from the cursor.
            var at = c.GetGlobalRect().GetCenter();
            InputDevice.Warped(at);
            GetViewport().WarpMouse(at);
        }
        if (sound) Audio.Sfx.Play("ui_hover");
    }

    /// <summary>Follows the target as it moves (a scrolled column, a re-laid screen): the pointer and the frame.</summary>
    private void Track()
    {
        if (!Usable(_focus))
        {
            Pointer = null;
            _frame.Clear();
            return;
        }
        var r = _focus!.GetGlobalRect();
        Pointer = r.GetCenter();
        _frame.Frame(r);
    }

    private void Release()
    {
        if (_focus is ItemSlotView slot && IsInstanceValid(slot))
        {
            slot.PadFocused = false;
            TooltipFollower.Current?.Hide(slot);
        }
        _focus = null;
        _heldDirection = null;
        Pointer = null;
        _frame.Clear();
    }
}
