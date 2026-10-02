using System.Collections.Generic;
using Godot;
using Strata.Game.Settings;

namespace Strata.Game.Menu;

/// <summary>
/// The Controls tab: the controller's feel (look sensitivity, inverted look, stick dead zone, aim assist), then every
/// action with two bindings to click: keyboard/mouse and controller. Clicking one listens for the next key or mouse
/// button, or the next controller button, stick or trigger push; Esc (or B while listening for a key) cancels.
/// </summary>
public sealed partial class SettingsScreen
{
    /// <summary>Seconds a binding ignores the controller after it starts listening (the A press that clicked it).</summary>
    private const double ListenGrace = 0.25;

    private readonly List<(string Action, Button Keys, Button Pad)> _bindingRows = new();
    private Button? _listening;

    /// <summary>A binding is waiting for its key or button: every event goes to it (the controller's menu steps wait too).</summary>
    public static bool Listening { get; private set; }
    private string? _listeningAction;
    private bool _listeningPad;
    private ulong _listenStartMs;

    private void Controls(VBoxContainer box)
    {
        box.AddChild(Header("Controller"));
        box.AddChild(MenuKit.Row("Look sensitivity", MenuKit.Slider(0.2, 3, 0.05, S.PadLookSensitivity, v => { S.PadLookSensitivity = (float)v; Changed(); }, v => $"{v:0.00}x"),
            "How fast the right stick turns the view."));
        box.AddChild(MenuKit.Row("Invert look Y", MenuKit.Toggle(S.PadInvertY, v => { S.PadInvertY = v; Changed(); })));
        box.AddChild(MenuKit.Row("Stick dead zone", MenuKit.Slider(0.05, 0.4, 0.01, S.PadDeadzone, v => { S.PadDeadzone = (float)v; Changed(); }, v => $"{v * 100:0}%"),
            "Stick travel ignored around the centre. Raise it if the view or the player drifts with the sticks at rest."));
        box.AddChild(MenuKit.Row("Aim assist", MenuKit.Toggle(S.AimAssist, v => { S.AimAssist = v; Changed(); }),
            "With a controller the crosshair slows down over enemies and is drawn gently toward them while you aim or move. The mouse never gets it."));
        box.AddChild(Header("Bindings — click one, then press a key, mouse button or controller button (Esc cancels)"));
        _bindingRows.Clear();
        foreach (var (action, label) in KeyBindings.Listed)
        {
            if (!InputMap.HasAction(action)) continue;
            var keys = MenuKit.Small(KeyBindings.Label(action, pad: false), () => { }, 180);
            var pad = MenuKit.Small(KeyBindings.Label(action, pad: true), () => { }, 180);
            string a = action;
            keys.Pressed += () => Listen(keys, a, pad: false);
            pad.Pressed += () => Listen(pad, a, pad: true);
            var pair = new HBoxContainer();
            pair.AddThemeConstantOverride("separation", 8);
            pair.AddChild(keys);
            pair.AddChild(pad);
            box.AddChild(MenuKit.Row(label, pair));
            _bindingRows.Add((action, keys, pad));
        }
        box.AddChild(MenuKit.Spacer(8));
        box.AddChild(MenuKit.Small("Reset all to defaults", () =>
        {
            KeyBindings.ResetAll(_services.Data);
            StopListening();
            foreach (var (action, keys, pad) in _bindingRows)
            {
                keys.Text = KeyBindings.Label(action, pad: false);
                pad.Text = KeyBindings.Label(action, pad: true);
            }
            Changed(); // the stick dead zone goes back onto the restored movement actions
        }, 240));
    }

    private void Listen(Button button, string action, bool pad)
    {
        StopListening();
        _listening = button;
        _listeningAction = action;
        _listeningPad = pad;
        _listenStartMs = Time.GetTicksMsec();
        Listening = true;
        button.Text = pad ? "Press a button..." : "Press a key...";
    }

    private void StopListening()
    {
        if (_listening != null && _listeningAction != null) _listening.Text = KeyBindings.Label(_listeningAction, _listeningPad);
        _listening = null;
        _listeningAction = null;
        Listening = false;
    }

    public override void _ExitTree() => Listening = false;

    public override void _Input(InputEvent @event)
    {
        if (_listening == null || _listeningAction == null) return;
        bool padEvent = @event is InputEventJoypadButton or InputEventJoypadMotion;
        bool pressed = @event is InputEventKey { Pressed: true, Echo: false } or InputEventMouseButton { Pressed: true }
            || (padEvent && KeyBindings.Describe(@event) != null && @event.IsPressed());
        if (!pressed) return;
        GetViewport().SetInputAsHandled();
        // The click that started listening lands here too; ignore a left click on the listening button itself, and the
        // controller's A press for a moment.
        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left } && _listening.GetGlobalRect().HasPoint(_listening.GetGlobalMousePosition()))
            return;
        if (padEvent && Time.GetTicksMsec() - _listenStartMs < ListenGrace * 1000) return;
        bool cancel = @event is InputEventKey { Keycode: Key.Escape } || (!_listeningPad && padEvent && @event.IsActionPressed("ui_cancel"));
        // A key never binds to the controller column, nor a controller button to the keyboard one.
        if (!cancel && padEvent != _listeningPad) return;
        if (!cancel) KeyBindings.Rebind(_listeningAction, @event);
        StopListening();
    }
}
