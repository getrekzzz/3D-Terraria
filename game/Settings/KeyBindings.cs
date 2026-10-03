using System.Collections.Generic;
using System.Linq;
using Godot;
using Strata.Core.Presentation;
using Strata.Game.Bootstrap;

namespace Strata.Game.Settings;

/// <summary>
/// Rebindable controls: defaults come from data/config/input.json (<see cref="InputSetup"/>); the player's changes are
/// stored in user://keybinds.cfg as the same "key:W" / "mouse:1" / "pad:a" / "axis:rt+" strings and applied over the
/// defaults at start. Keyboard/mouse and controller bindings are separate: rebinding one leaves the other alone, and a
/// saved file without controller bindings (older ones) keeps the default controller layout.
/// </summary>
public static class KeyBindings
{
    private const string FilePath = "user://keybinds.cfg";

    /// <summary>Actions shown in the controls menu, with their labels, in order.</summary>
    public static readonly (string Action, string Label)[] Listed =
    {
        ("move_forward", "Move forward"), ("move_back", "Move back"), ("move_left", "Move left"), ("move_right", "Move right"),
        ("look_left", "Look left"), ("look_right", "Look right"), ("look_up", "Look up"), ("look_down", "Look down"),
        ("jump", "Jump"), ("sprint", "Sprint"), ("crouch", "Drop through platforms"),
        ("use_primary", "Use item / attack"), ("use_secondary", "Interact / place"), ("interact", "Interact"),
        ("inventory", "Inventory"), ("housing", "Housing menu"), ("map", "World map"), ("guide_book", "Guide book"), ("toggle_camera", "Toggle first/third person"), ("precision_mode", "Precision (small blocks)"),
        ("cycle_shape", "Cycle block shape"), ("rotate_shape", "Rotate block shape"), ("drop_item", "Drop item"), ("grapple", "Grappling hook"),
        ("quick_mount", "Quick mount"), ("smart_cursor", "Smart cursor"), ("emote_wheel", "Emote wheel"),
        ("quick_heal", "Quick heal"), ("quick_mana", "Quick mana"), ("quick_buff", "Quick buff"),
        ("pause", "Menu / pause"), ("chat", "Chat history"), ("console", "Console"), ("debug_overlay", "Performance overlay"),
        ("photo_mode", "Photo mode"), ("photo_shot", "Photo mode: take picture"),
        ("hotbar_1", "Hotbar 1"), ("hotbar_2", "Hotbar 2"), ("hotbar_3", "Hotbar 3"), ("hotbar_4", "Hotbar 4"), ("hotbar_5", "Hotbar 5"),
        ("hotbar_6", "Hotbar 6"), ("hotbar_7", "Hotbar 7"), ("hotbar_8", "Hotbar 8"), ("hotbar_9", "Hotbar 9"), ("hotbar_10", "Hotbar 10"),
        ("hotbar_next", "Next hotbar slot"), ("hotbar_prev", "Previous hotbar slot"),
        ("ui_up", "Menus: up"), ("ui_down", "Menus: down"), ("ui_left", "Menus: left"), ("ui_right", "Menus: right"),
        ("ui_accept", "Menus: select / click"), ("ui_cancel", "Menus: back / close"), ("ui_split", "Inventory: take one (right click)"),
        ("ui_quick_move", "Inventory: quick move (Shift-click)"), ("ui_favorite", "Inventory: favourite"),
    };

    /// <summary>Applies the saved bindings over the registered defaults, keyboard/mouse and controller separately.</summary>
    public static void Load()
    {
        var file = new ConfigFile();
        if (file.Load(FilePath) != Error.Ok) return;
        foreach (var action in file.GetSectionKeys("bindings"))
        {
            if (!InputMap.HasAction(action)) continue;
            var saved = file.GetValue("bindings", action).AsStringArray().Where(s => InputSetup.Parse(s) != null).ToList();
            foreach (bool pad in new[] { false, true })
            {
                var mine = saved.Where(s => IsPad(s) == pad).ToList();
                if (mine.Count == 0) continue; // nothing saved for this device: its defaults stay
                Erase(action, pad);
                foreach (var s in mine) InputMap.ActionAddEvent(action, InputSetup.Parse(s)!);
            }
        }
    }

    /// <summary>
    /// Replaces an action's bindings of the same device as <paramref name="input"/> (keyboard/mouse or controller) with
    /// it and saves; the other device's bindings stay.
    /// </summary>
    public static void Rebind(string action, InputEvent input)
    {
        if (Describe(input) is not { } binding) return;
        Erase(action, IsPad(binding));
        InputMap.ActionAddEvent(action, InputSetup.Parse(binding)!);
        Save();
    }

    /// <summary>Restores the defaults from data/config/input.json and forgets the saved changes.</summary>
    public static void ResetAll(GodotDataSource data)
    {
        InputSetup.Register(data);
        DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(FilePath));
    }

    /// <summary>Every binding of an action, "W / Left Stick Up".</summary>
    public static string Label(string action) => Join(action, null);

    /// <summary>The bindings of one device: keyboard/mouse (<paramref name="pad"/> false) or controller.</summary>
    public static string Label(string action, bool pad) => Join(action, pad);

    /// <summary>
    /// What a prompt shows for an action on the device in use (<see cref="UI.InputDevice"/>): its first binding there
    /// ("E" on the keyboard, "X" on a controller), or every binding when that device has none.
    /// </summary>
    public static string Prompt(string action)
    {
        if (!InputMap.HasAction(action)) return "";
        bool pad = UI.InputDevice.UsingPad;
        foreach (var e in InputMap.ActionGetEvents(action))
            if (Describe(e) is { } d && IsPad(d) == pad) return Pretty(d);
        return Label(action);
    }

    /// <summary>
    /// The binding string of a key, mouse button, controller button or a firmly pushed stick or trigger ("key:W",
    /// "mouse:1", "pad:a", "axis:left_y-"), or null for other input.
    /// </summary>
    public static string? Describe(InputEvent e) => e switch
    {
        InputEventKey k => "key:" + OS.GetKeycodeString(k.PhysicalKeycode != Key.None ? k.PhysicalKeycode : k.Keycode),
        InputEventMouseButton m => $"mouse:{(int)m.ButtonIndex}",
        InputEventJoypadButton j when (int)j.ButtonIndex >= 0 && (int)j.ButtonIndex < InputBinding.PadButtons.Length =>
            InputBinding.PadButton((int)j.ButtonIndex).ToString(),
        InputEventJoypadMotion a when (int)a.Axis >= 0 && (int)a.Axis < InputBinding.PadAxes.Length && Mathf.Abs(a.AxisValue) >= 0.5f
                                      && ((int)a.Axis < 4 || a.AxisValue > 0) =>
            InputBinding.PadAxis((int)a.Axis, a.AxisValue < 0 ? -1 : 1).ToString(),
        _ => null,
    };

    /// <summary>A binding string is a controller's ("pad:…" or "axis:…").</summary>
    public static bool IsPad(string binding) => binding.StartsWith("pad:") || binding.StartsWith("axis:");

    /// <summary>Removes an action's controller bindings (<paramref name="pad"/>) or its keyboard and mouse ones.</summary>
    private static void Erase(string action, bool pad)
    {
        foreach (var e in InputMap.ActionGetEvents(action))
            if ((e is InputEventJoypadButton or InputEventJoypadMotion) == pad) InputMap.ActionEraseEvent(action, e);
    }

    private static string Join(string action, bool? pad)
    {
        if (!InputMap.HasAction(action)) return "—";
        var names = new List<string>();
        foreach (var e in InputMap.ActionGetEvents(action))
            if (Describe(e) is { } d && (pad == null || IsPad(d) == pad)) names.Add(Pretty(d));
        return names.Count == 0 ? "—" : string.Join(" / ", names);
    }

    private static string Pretty(string binding) => binding switch
    {
        "mouse:1" => "Left Mouse",
        "mouse:2" => "Right Mouse",
        "mouse:3" => "Middle Mouse",
        "mouse:4" => "Wheel Up",
        "mouse:5" => "Wheel Down",
        _ when binding.StartsWith("mouse:") => $"Mouse {binding.Substring(6)}",
        _ when IsPad(binding) => InputBinding.TryParse(binding, out var b, out _) ? b.PadLabel ?? binding : binding,
        _ => binding.Substring(4),
    };

    private static void Save()
    {
        var file = new ConfigFile();
        foreach (var (action, _) in Listed)
        {
            if (!InputMap.HasAction(action)) continue;
            var list = new List<string>();
            foreach (var e in InputMap.ActionGetEvents(action))
                if (Describe(e) is { } d) list.Add(d);
            file.SetValue("bindings", action, list.ToArray());
        }
        file.Save(FilePath);
    }
}
