using Godot;
using Strata.Game.Settings;

namespace Strata.Game.Menu;

public sealed partial class SettingsScreen
{
    private Button? _listening;
    private string? _listeningAction;

    private void Controls(VBoxContainer box)
    {
        box.AddChild(Header("Key bindings — click a binding, then press a key or mouse button (Esc cancels)"));
        foreach (var (action, label) in KeyBindings.Listed)
        {
            if (!InputMap.HasAction(action)) continue;
            var button = MenuKit.Small(KeyBindings.Label(action), () => { }, 240);
            string a = action;
            button.Pressed += () => Listen(button, a);
            box.AddChild(MenuKit.Row(label, button));
        }
        box.AddChild(MenuKit.Spacer(8));
        box.AddChild(MenuKit.Small("Reset all to defaults", () =>
        {
            KeyBindings.ResetAll(_services.Data);
            foreach (var row in box.GetChildren())
                if (row is HBoxContainer h && h.GetChildCount() == 2 && h.GetChild(1) is Button b)
                    foreach (var (action, label) in KeyBindings.Listed)
                        if (((Label)h.GetChild(0)).Text == label) b.Text = KeyBindings.Label(action);
        }, 240));
    }

    private void Listen(Button button, string action)
    {
        if (_listening != null && _listeningAction != null) _listening.Text = KeyBindings.Label(_listeningAction);
        _listening = button;
        _listeningAction = action;
        button.Text = "Press a key...";
    }

    public override void _Input(InputEvent @event)
    {
        if (_listening == null || _listeningAction == null) return;
        bool pressed = @event is InputEventKey { Pressed: true, Echo: false } || @event is InputEventMouseButton { Pressed: true };
        if (!pressed) return;
        GetViewport().SetInputAsHandled();
        // The click that started listening lands here too; ignore a left click on the listening button itself.
        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left } && _listening.GetGlobalRect().HasPoint(_listening.GetGlobalMousePosition()))
            return;
        if (@event is not InputEventKey { Keycode: Key.Escape }) KeyBindings.Rebind(_listeningAction, @event);
        _listening.Text = KeyBindings.Label(_listeningAction);
        _listening = null;
        _listeningAction = null;
    }
}
