using System.Text.Json;
using Godot;
using Strata.Core.Content.Loading;

namespace Strata.Game.Bootstrap;

/// <summary>Registers input actions from data/config/input.json ("key:W", "mouse:1").</summary>
public static class InputSetup
{
    public static void Register(IDataSource data)
    {
        using var doc = JsonDocument.Parse(data.ReadText("data/config/input.json"));
        foreach (var action in doc.RootElement.GetProperty("actions").EnumerateObject())
        {
            if (!InputMap.HasAction(action.Name)) InputMap.AddAction(action.Name, 0.2f);
            InputMap.ActionEraseEvents(action.Name);
            foreach (var binding in action.Value.EnumerateArray())
            {
                var ev = Parse(binding.GetString()!);
                if (ev != null) InputMap.ActionAddEvent(action.Name, ev);
                else GD.PushWarning($"Unknown binding '{binding.GetString()}' for {action.Name}");
            }
        }
    }

    public static InputEvent? Parse(string binding)
    {
        var parts = binding.Split(':', 2);
        if (parts.Length != 2) return null;
        switch (parts[0])
        {
            case "key":
                var key = OS.FindKeycodeFromString(parts[1]);
                return key == Key.None ? null : new InputEventKey { PhysicalKeycode = key };
            case "mouse":
                return int.TryParse(parts[1], out int b) ? new InputEventMouseButton { ButtonIndex = (MouseButton)b } : null;
            default:
                return null;
        }
    }
}
