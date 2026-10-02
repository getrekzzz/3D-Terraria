using Godot;
using Strata.Core.Presentation;
using Strata.Game.Settings;

namespace Strata.Game.UI;

/// <summary>
/// Controller support that is always there, on the title screen and in a world (a child of the window, after the game
/// root, so it sees every event first): each event passes <see cref="InputDevice"/>. On menus (the title screens, the
/// pause menu, settings, a world's modal screens) the D-pad and the left stick step the engine's focus one control at a
/// time, repeating while held (the raw stick would step on every small change of its tilt), and the first step with
/// nothing focused selects the topmost menu's first control; A presses, sliders slide, B presses the menu's Back (or
/// goes back as Esc does), and the focused control is framed in gold (<see cref="PadFocusFrame"/>). The stick dead zone setting is applied to the
/// movement actions here. Gameplay screens have their own cursor (<see cref="PadCursor"/>).
/// </summary>
public sealed partial class PadInput : CanvasLayer
{
    private static readonly StringName[] Steps = { "ui_up", "ui_down", "ui_left", "ui_right" };
    private static readonly StringName[] MoveActions = { "move_forward", "move_back", "move_left", "move_right" };
    private static readonly StringName Accept = "ui_accept";
    private static readonly StringName Cancel = "ui_cancel";
    private static readonly StringName Pause = "pause";

    private readonly bool[] _dpad = new bool[4];
    private PadFocusFrame _frame = null!;
    private System.Func<bool> _inWorld = () => false;
    private Vector2 _stick;
    private NavDirection? _held;
    private double _repeatWait;

    public void Initialize(System.Func<bool> inWorld)
    {
        _inWorld = inWorld;
        Name = "PadInput";
        Layer = 120; // over every menu
        AddChild(_frame = new PadFocusFrame());
        ApplyDeadzone(UserSettings.Current);
        UserSettings.Current.Changed += ApplyDeadzone;
    }

    public override void _ExitTree() => UserSettings.Current.Changed -= ApplyDeadzone;

    /// <summary>A menu, not the world, has the controller: no world, or a modal screen over it.</summary>
    private bool OnMenu => (!_inWorld() || (InputGate.GameplayBlocked && !InputGate.PadNavigating)) && !InputGate.TextEntry
        && !Menu.SettingsScreen.Listening;

    public override void _Input(InputEvent e)
    {
        InputDevice.Observe(e);
        // The D-pad and the left stick are followed everywhere, so a menu opening under a held stick does not step.
        int dpad = e is InputEventJoypadButton button ? (int)button.ButtonIndex - (int)JoyButton.DpadUp : -1;
        if ((uint)dpad < 4) _dpad[dpad] = ((InputEventJoypadButton)e).Pressed;
        if (e is InputEventJoypadMotion { Axis: JoyAxis.LeftX } x) _stick.X = x.AxisValue;
        if (e is InputEventJoypadMotion { Axis: JoyAxis.LeftY } y) _stick.Y = y.AxisValue;
        if (e is not (InputEventJoypadButton or InputEventJoypadMotion) || !OnMenu) return;
        var viewport = GetViewport();
        if ((uint)dpad < 4 || e is InputEventJoypadMotion { Axis: JoyAxis.LeftX or JoyAxis.LeftY })
        {
            viewport.SetInputAsHandled(); // stepped in _Process
            return;
        }
        // A with nothing focused only shows where the controller is.
        if (e.IsActionPressed(Accept) && !Focused(viewport) && FocusFirst()) viewport.SetInputAsHandled();
    }

    /// <summary>
    /// B on a menu, when nothing took it: the topmost menu's Back (or No) button when it has one (the title screens, a
    /// confirmation, settings, which save on Back), else Esc in a world (the pause menu and the map close).
    /// </summary>
    public override void _UnhandledInput(InputEvent e)
    {
        if (e is not InputEventJoypadButton || !e.IsActionPressed(Cancel) || !OnMenu) return;
        GetViewport().SetInputAsHandled();
        Button? back = null;
        int layer = int.MinValue;
        FindBack(GetTree().Root, 0, ref back, ref layer);
        if (back != null) back.EmitSignal(BaseButton.SignalName.Pressed);
        else if (_inWorld())
        {
            Input.ParseInputEvent(new InputEventAction { Action = Pause, Pressed = true });
            Input.ParseInputEvent(new InputEventAction { Action = Pause, Pressed = false });
        }
    }

    /// <summary>The visible Back or No button on the highest layer.</summary>
    private static void FindBack(Node node, int layer, ref Button? best, ref int bestLayer)
    {
        if (node is CanvasLayer canvas)
        {
            if (!canvas.Visible) return;
            layer = canvas.Layer;
        }
        if (node is CanvasItem { Visible: false } || node is Node3D) return;
        if (node is Button { Disabled: false, Text: "Back" or "No" } b && layer > bestLayer && b.IsVisibleInTree())
        {
            best = b;
            bestLayer = layer;
        }
        int count = node.GetChildCount();
        for (int i = 0; i < count; i++) FindBack(node.GetChild(i), layer, ref best, ref bestLayer);
    }

    public override void _Process(double delta)
    {
        bool menu = InputDevice.UsingPad && OnMenu;
        Repeat(menu, delta);
        // The menu focus is framed while the controller is in use; the gameplay screens frame their own cursor.
        if (menu && GetViewport().GuiGetFocusOwner() is { } owner && owner.IsVisibleInTree() && owner is not LineEdit)
            _frame.Frame(owner.GetGlobalRect());
        else _frame.Clear();
    }

    /// <summary>A new direction on the D-pad or the stick steps at once; held, it repeats (data/config/input.json, "pad").</summary>
    private void Repeat(bool menu, double delta)
    {
        var held = menu ? HeldDirection() : null;
        var pad = Bootstrap.InputSetup.Config.Pad;
        if (held != _held)
        {
            _held = held;
            _repeatWait = pad.RepeatDelay;
            if (held is { } first) Step(first);
            return;
        }
        if (held is not { } dir || (_repeatWait -= delta) > 0) return;
        _repeatWait = Mathf.Max(_repeatWait + pad.RepeatInterval, 0);
        Step(dir);
    }

    private NavDirection? HeldDirection()
    {
        for (int i = 0; i < 4; i++)
            if (_dpad[i]) return (NavDirection)i; // DpadUp, Down, Left, Right: the same order as NavDirection
        if (_stick.LengthSquared() < 0.25f) return null;
        return Mathf.Abs(_stick.X) > Mathf.Abs(_stick.Y)
            ? (_stick.X > 0 ? NavDirection.Right : NavDirection.Left)
            : (_stick.Y > 0 ? NavDirection.Down : NavDirection.Up);
    }

    /// <summary>One focus step as the engine's menu navigation takes it (a slider under the focus slides instead).</summary>
    private void Step(NavDirection dir)
    {
        if (!Focused(GetViewport()))
        {
            FocusFirst();
            return;
        }
        var action = Steps[(int)dir];
        Input.ParseInputEvent(new InputEventAction { Action = action, Pressed = true });
        Input.ParseInputEvent(new InputEventAction { Action = action, Pressed = false });
    }

    private static bool Focused(Viewport viewport) => viewport.GuiGetFocusOwner() is { } owner && owner.IsVisibleInTree();

    private bool FocusFirst()
    {
        if (FirstFocusable(GetTree().Root) is not { } first) return false;
        first.GrabFocus();
        return true;
    }

    /// <summary>
    /// The control the controller starts on: of the visible, enabled controls that take focus, the one on the highest
    /// layer, then topmost, then leftmost, outside the 3D world. Runs only on the press that needs it.
    /// </summary>
    public static Control? FirstFocusable(Node root)
    {
        Control? best = null;
        int bestLayer = int.MinValue;
        Find(root, 0, ref best, ref bestLayer);
        return best;
    }

    private static void Find(Node node, int layer, ref Control? best, ref int bestLayer)
    {
        if (node is CanvasLayer canvas)
        {
            if (!canvas.Visible) return;
            layer = canvas.Layer;
        }
        // The 3D world (its HUD screens have their own cursor) is not searched: menus live outside it.
        if (node is CanvasItem { Visible: false } || node is Node3D) return;
        if (node is Control c && c.FocusMode == Control.FocusModeEnum.All && c is not LineEdit && c.IsVisibleInTree()
            && !(c is BaseButton { Disabled: true }) && Better(c, layer, best, bestLayer))
        {
            best = c;
            bestLayer = layer;
        }
        int count = node.GetChildCount();
        for (int i = 0; i < count; i++) Find(node.GetChild(i), layer, ref best, ref bestLayer);
    }

    private static bool Better(Control c, int layer, Control? best, int bestLayer)
    {
        if (best == null || layer != bestLayer) return best == null || layer > bestLayer;
        var a = c.GetGlobalRect().Position;
        var b = best.GetGlobalRect().Position;
        return a.Y < b.Y - 1 || (Mathf.Abs(a.Y - b.Y) <= 1 && a.X < b.X);
    }

    /// <summary>The dead zone setting for walking with the left stick (the look stick shapes its own, CameraRig).</summary>
    private static void ApplyDeadzone(UserSettings s)
    {
        float zone = Mathf.Clamp(s.PadDeadzone, 0.05f, 0.5f);
        foreach (var action in MoveActions)
            if (InputMap.HasAction(action)) InputMap.ActionSetDeadzone(action, zone);
    }
}
