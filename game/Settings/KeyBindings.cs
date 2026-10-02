using System.Collections.Generic;
using System.Linq;
using Godot;
using Strata.Game.Bootstrap;

namespace Strata.Game.Settings;

/// <summary>
/// Rebindable controls: defaults come from data/config/input.json (<see cref="InputSetup"/>); the player's changes are
/// stored in user://keybinds.cfg as the same "key:W" / "mouse:1" strings and applied over the defaults at start.
/// </summary>
public static class KeyBindings
{
    private const string FilePath = "user://keybinds.cfg";

    /// <summary>Actions shown in the controls menu, with their labels, in order.</summary>
    public static readonly (string Action, string Label)[] Listed =
    {
        ("move_forward", "Move forward"), ("move_back", "Move back"), ("move_left", "Move left"), ("move_right", "Move right"),
        ("jump", "Jump"), ("sprint", "Sprint"), ("crouch", "Drop through platforms"),
        ("use_primary", "Use item / attack"), ("use_secondary", "Interact / place"), ("interact", "Interact"),
        ("inventory", "Inventory"), ("toggle_camera", "Toggle first/third person"), ("precision_mode", "Precision (small blocks)"),
        ("cycle_shape", "Cycle block shape"), ("rotate_shape", "Rotate block shape"), ("drop_item", "Drop item"), ("grapple", "Grappling hook"),
        ("quick_mount", "Quick mount"), ("pause", "Menu / pause"), ("console", "Console"), ("debug_overlay", "Performance overlay"),
        ("hotbar_1", "Hotbar 1"), ("hotbar_2", "Hotbar 2"), ("hotbar_3", "Hotbar 3"), ("hotbar_4", "Hotbar 4"), ("hotbar_5", "Hotbar 5"),
        ("hotbar_6", "Hotbar 6"), ("hotbar_7", "Hotbar 7"), ("hotbar_8", "Hotbar 8"), ("hotbar_9", "Hotbar 9"), ("hotbar_10", "Hotbar 10"),
    };

    /// <summary>Applies the saved bindings over the registered defaults.</summary>
    public static void Load()
    {
        var file = new ConfigFile();
        if (file.Load(FilePath) != Error.Ok) return;
        foreach (var action in file.GetSectionKeys("bindings"))
        {
            if (!InputMap.HasAction(action)) continue;
            var bindings = file.GetValue("bindings", action).AsStringArray();
            var events = bindings.Select(InputSetup.Parse).Where(e => e != null).ToList();
            if (events.Count == 0) continue;
            InputMap.ActionEraseEvents(action);
            foreach (var e in events) InputMap.ActionAddEvent(action, e!);
        }
    }

    /// <summary>Replaces an action's bindings with one key or mouse button and saves.</summary>
    public static void Rebind(string action, InputEvent input)
    {
        if (Describe(input) is not { } binding) return;
        InputMap.ActionEraseEvents(action);
        InputMap.ActionAddEvent(action, InputSetup.Parse(binding)!);
        Save();
    }

    /// <summary>Restores the defaults from data/config/input.json and forgets the saved changes.</summary>
    public static void ResetAll(GodotDataSource data)
    {
        InputSetup.Register(data);
        DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(FilePath));
    }

    public static string Label(string action)
    {
        var names = InputMap.ActionGetEvents(action).Select(Describe).Where(d => d != null).Select(Pretty).ToList();
        return names.Count == 0 ? "—" : string.Join(" / ", names);
    }

    /// <summary>The binding string of a key or mouse button ("key:W", "mouse:1"), or null for other input.</summary>
    public static string? Describe(InputEvent e) => e switch
    {
        InputEventKey k => "key:" + OS.GetKeycodeString(k.PhysicalKeycode != Key.None ? k.PhysicalKeycode : k.Keycode),
        InputEventMouseButton m => $"mouse:{(int)m.ButtonIndex}",
        _ => null,
    };

    private static string Pretty(string? binding) => binding switch
    {
        "mouse:1" => "Left Mouse",
        "mouse:2" => "Right Mouse",
        "mouse:3" => "Middle Mouse",
        "mouse:4" => "Wheel Up",
        "mouse:5" => "Wheel Down",
        null => "",
        _ when binding.StartsWith("mouse:") => $"Mouse {binding.Substring(6)}",
        _ => binding.Substring(4),
    };

    private static void Save()
    {
        var file = new ConfigFile();
        foreach (var (action, _) in Listed)
        {
            var list = new List<string>();
            foreach (var e in InputMap.ActionGetEvents(action))
                if (Describe(e) is { } d) list.Add(d);
            file.SetValue("bindings", action, list.ToArray());
        }
        file.Save(FilePath);
    }
}
