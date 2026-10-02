using Godot;
using Strata.Core.Presentation;

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

    /// <summary>The UI scale in effect: the setting, lowered when the window is too small to hold the layout at it.</summary>
    public static float EffectiveUiScale { get; private set; } = 1f;

    /// <summary>True when the window size lowered the UI scale below the setting.</summary>
    public static bool UiScaleClamped { get; private set; }

    /// <summary>
    /// Scales every 2D interface (menus, HUD) by the UI scale setting, clamped so the logical viewport stays at least
    /// 1024×600 (<see cref="UiScale"/>): raising the scale can never push panels off a small window. Called again
    /// whenever the window is resized.
    /// </summary>
    public static void ApplyUiScale(Window root, UserSettings s) => ApplyUiScale(root, s.UiScale);

    public static void ApplyUiScale(Window root, float setting)
    {
        EffectiveUiScale = Effective(setting, root.Size);
        UiScaleClamped = EffectiveUiScale < Mathf.Clamp(setting, UiScale.Min, UiScale.Max) - 0.001f;
        if (!Mathf.IsEqualApprox(root.ContentScaleFactor, EffectiveUiScale)) root.ContentScaleFactor = EffectiveUiScale;
    }

    /// <summary>The UI scale a window of <paramref name="window"/> pixels gets for <paramref name="setting"/>.</summary>
    public static float Effective(float setting, Vector2I window) =>
        DisplayServer.GetName() != "headless" ? UiScale.Effective(setting, window.X, window.Y)
            : UI.LayoutWidgets.SimulatedWindow is { } sim ? UiScale.Effective(setting, sim.X, sim.Y) // the window a test lays out for
            : UiScale.Effective(setting, 0, 0); // no real window to fit
}
