using Godot;
using Strata.Core.Content.Loading;
using Strata.Core.Presentation;

namespace Strata.Game.Bootstrap;

/// <summary>
/// Registers the input actions of data/config/input.json (<see cref="InputConfig"/>): keys ("key:W"), mouse buttons
/// ("mouse:1"), controller buttons ("pad:a") and stick or trigger directions ("axis:left_y-"). Controller bindings
/// listen to every connected controller. The menu actions (ui_up … ui_cancel) are replaced too, so a controller drives
/// the menus and they rebind like the rest.
/// </summary>
public static class InputSetup
{
    /// <summary>The engine's device id that matches every controller.</summary>
    private const int AllDevices = -1;

    /// <summary>The defaults as loaded (the controller's look speed, curve, cursor repeat and aim assist live here).</summary>
    public static InputConfig Config { get; private set; } = new();

    public static void Register(IDataSource data)
    {
        var config = InputConfig.Load(data);
        foreach (var problem in config.Validate()) GD.PushWarning($"{InputConfig.Path}: {problem}");
        Config = config;
        foreach (var (action, bindings) in config.Actions)
        {
            if (!InputMap.HasAction(action)) InputMap.AddAction(action, 0.2f);
            InputMap.ActionEraseEvents(action);
            foreach (var binding in bindings)
            {
                var ev = Parse(binding);
                if (ev != null) InputMap.ActionAddEvent(action, ev);
                else GD.PushWarning($"Unknown binding '{binding}' for {action}");
            }
        }
    }

    /// <summary>The input event a binding string listens for, or null when it is not one.</summary>
    public static InputEvent? Parse(string binding) => InputBinding.TryParse(binding, out var b, out _) ? Create(b) : null;

    public static InputEvent? Create(InputBinding b)
    {
        switch (b.Kind)
        {
            case BindingKind.Key:
                var key = OS.FindKeycodeFromString(b.Name);
                return key == Key.None ? null : new InputEventKey { PhysicalKeycode = key };
            case BindingKind.Mouse:
                return new InputEventMouseButton { ButtonIndex = (MouseButton)b.Code };
            case BindingKind.PadButton:
                return new InputEventJoypadButton { ButtonIndex = (JoyButton)b.Code, Device = AllDevices };
            default:
                return new InputEventJoypadMotion { Axis = (JoyAxis)b.Code, AxisValue = b.Sign, Device = AllDevices };
        }
    }
}
