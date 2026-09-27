using Godot;

namespace Strata.Game.Settings;

/// <summary>Applies the window and frame-rate settings: window mode, vsync, frame limit and UI scale.</summary>
public static class DisplaySettings
{
    public static readonly string[] WindowModes = { "windowed", "fullscreen", "borderless" };

    public static void Apply(UserSettings s)
    {
        if (DisplayServer.GetName() == "headless") return;
        var mode = s.WindowMode switch
        {
            "fullscreen" => DisplayServer.WindowMode.ExclusiveFullscreen,
            "borderless" => DisplayServer.WindowMode.Fullscreen,
            _ => DisplayServer.WindowMode.Windowed,
        };
        if (DisplayServer.WindowGetMode() != mode) DisplayServer.WindowSetMode(mode);
        DisplayServer.WindowSetVsyncMode(s.VSync ? DisplayServer.VSyncMode.Enabled : DisplayServer.VSyncMode.Disabled);
        Engine.MaxFps = Mathf.Max(0, s.MaxFps);
    }

    /// <summary>Scales every 2D interface (menus, HUD) by the UI scale setting.</summary>
    public static void ApplyUiScale(Window root, UserSettings s) => root.ContentScaleFactor = Mathf.Clamp(s.UiScale, 0.5f, 3f);
}
