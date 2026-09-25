using System.Collections.Generic;

namespace Strata.Game.UI;

/// <summary>
/// Tracks open UI layers that capture the keyboard/mouse (console, inventory, chests). While any is open,
/// gameplay input (movement, camera look, item use) is ignored.
/// </summary>
public static class InputGate
{
    private static readonly HashSet<string> Open = new();

    public static bool GameplayBlocked => Open.Count > 0;

    /// <summary>True when a text field has focus (even hotkeys must be ignored).</summary>
    public static bool TextEntry { get; set; }

    public static void SetOpen(string layer, bool open)
    {
        if (open) Open.Add(layer);
        else Open.Remove(layer);
        Godot.Input.MouseMode = GameplayBlocked ? Godot.Input.MouseModeEnum.Visible : Godot.Input.MouseModeEnum.Captured;
    }

    public static bool IsOpen(string layer) => Open.Contains(layer);
}
