using System;
using System.Linq;
using Godot;
using Strata.Game.Bootstrap;
using Strata.Game.Settings;

namespace Strata.Game.Menu;

/// <summary>
/// The settings menu (title screen and pause menu): Gameplay, Video, Audio, Controls and Interface tabs. Every change
/// applies immediately; settings are saved when the screen is closed.
/// </summary>
public sealed partial class SettingsScreen : PanelContainer
{
    private GameServices _services = null!;
    private Viewport _viewport = null!;
    private UserSettings S => UserSettings.Current;

    public void Initialize(GameServices services, Viewport viewport, Action back)
    {
        _services = services;
        _viewport = viewport;
        // fits the smallest logical viewport the UI scale guard allows (1024×600) with a margin; the tabs scroll
        CustomMinimumSize = new Vector2(900, 560);
        var style = UI.UiTheme.Panel(0.96f);
        style.ContentMarginLeft = style.ContentMarginRight = 20;
        style.ContentMarginTop = style.ContentMarginBottom = 16;
        AddThemeStyleboxOverride("panel", style);
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 10);
        AddChild(box);
        box.AddChild(MenuKit.Title("Settings", 40));
        var tabs = new TabContainer { SizeFlagsVertical = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 400) };
        tabs.AddThemeFontSizeOverride("font_size", 18);
        box.AddChild(tabs);
        tabs.AddChild(Tab("Gameplay", Gameplay));
        tabs.AddChild(Tab("Video", Video));
        tabs.AddChild(Tab("Audio", Audio));
        tabs.AddChild(Tab("Controls", Controls));
        tabs.AddChild(Tab("Interface", Interface));
        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        buttons.AddThemeConstantOverride("separation", 24);
        buttons.AddChild(MenuKit.Big("Back", () =>
        {
            S.Save();
            back();
        }, 26));
        box.AddChild(buttons);
    }

    private static ScrollContainer Tab(string name, Action<VBoxContainer> build)
    {
        var scroll = new ScrollContainer { Name = name, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        var margin = new MarginContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        margin.AddThemeConstantOverride("margin_left", 12);
        margin.AddThemeConstantOverride("margin_right", 18);
        margin.AddThemeConstantOverride("margin_top", 10);
        var box = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        box.AddThemeConstantOverride("separation", 8);
        margin.AddChild(box);
        scroll.AddChild(margin);
        build(box);
        return scroll;
    }

    private static Label Header(string text)
    {
        var l = MenuKit.Text(text, 20, MenuKit.Gold);
        l.CustomMinimumSize = new Vector2(0, 30);
        return l;
    }

    private void Changed() => S.Apply();

    private void Gameplay(VBoxContainer box)
    {
        box.AddChild(Header("Building and mining"));
        box.AddChild(MenuKit.Row("Block size", MenuKit.Choice(new[] { "Large (1 m blocks)", "Small (0.5 m cells)" }, S.SmallBlocks ? 1 : 0, i => { S.SmallBlocks = i == 1; Changed(); }),
            "Blocks are mined and placed as 1 m grid blocks, or as single 0.5 m cells. Holding Alt swaps while held."));
        box.AddChild(MenuKit.Row("Smart dig", MenuKit.Toggle(S.SmartDig, v => { S.SmartDig = v; Changed(); }),
            "Looking steeply down with a pickaxe digs the ground under your feet."));
        box.AddChild(Header("Camera"));
        box.AddChild(MenuKit.Row("First person by default", MenuKit.Toggle(S.FirstPerson, v => { S.FirstPerson = v; Changed(); })));
        box.AddChild(MenuKit.Row("Field of view", MenuKit.Slider(50, 110, 1, S.FieldOfView, v => { S.FieldOfView = (float)v; Changed(); }, v => $"{v:0}°")));
        box.AddChild(MenuKit.Row("Camera distance", MenuKit.Slider(1.5, 9, 0.1, S.CameraDistance, v => { S.CameraDistance = (float)v; Changed(); }, v => $"{v:0.0} m")));
        box.AddChild(MenuKit.Row("Mouse sensitivity", MenuKit.Slider(0.1, 3, 0.05, S.MouseSensitivity, v => { S.MouseSensitivity = (float)v; Changed(); }, v => $"{v:0.00}x")));
        box.AddChild(MenuKit.Row("Invert mouse Y", MenuKit.Toggle(S.InvertY, v => { S.InvertY = v; Changed(); })));
        box.AddChild(Header("World"));
        box.AddChild(MenuKit.Row("Pause when inventory is open", MenuKit.Toggle(S.AutoPause, v => { S.AutoPause = v; Changed(); })));
        box.AddChild(MenuKit.Row("Damage numbers", MenuKit.Toggle(S.DamageNumbers, v => { S.DamageNumbers = v; Changed(); })));
    }

    private void Audio(VBoxContainer box)
    {
        box.AddChild(Header("Volume"));
        string Percent(double v) => $"{v * 100:0}%";
        box.AddChild(MenuKit.Row("Master", MenuKit.Slider(0, 1, 0.01, S.MasterVolume, v => { S.MasterVolume = (float)v; Changed(); }, Percent)));
        box.AddChild(MenuKit.Row("Music", MenuKit.Slider(0, 1, 0.01, S.MusicVolume, v => { S.MusicVolume = (float)v; Changed(); }, Percent)));
        box.AddChild(MenuKit.Row("Sound", MenuKit.Slider(0, 1, 0.01, S.SoundVolume, v => { S.SoundVolume = (float)v; Changed(); }, Percent)));
        box.AddChild(MenuKit.Row("Ambient", MenuKit.Slider(0, 1, 0.01, S.AmbientVolume, v => { S.AmbientVolume = (float)v; Changed(); }, Percent)));

        box.AddChild(Header("Output"));
        var devices = Godot.AudioServer.GetOutputDeviceList();
        int current = System.Math.Max(0, System.Array.IndexOf(devices, S.AudioDevice));
        box.AddChild(MenuKit.Row("Output device", MenuKit.Choice(devices, current, i => { S.AudioDevice = devices[i]; Changed(); }),
            "Where the game's sound goes. \"Default\" follows the Windows default device."));
        var status = MenuKit.Text(Status(), 15);
        status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        var test = MenuKit.Small("Test sound", () =>
        {
            Strata.Game.Audio.AudioDiagnostics.PlayTest(this);
            status.Text = Status(probe: true);
        });
        box.AddChild(MenuKit.Row("Check", test, "Plays a chime made by the game itself, then a game sound, and checks every sound file."));
        box.AddChild(status);
    }

    /// <summary>What the audio system is doing; with <paramref name="probe"/>, also whether every sound file loads.</summary>
    private static string Status(bool probe = false)
    {
        var text = $"Using {Strata.Game.Audio.AudioDiagnostics.Device()}";
        if (Strata.Game.Audio.AudioDiagnostics.Silent) text += "\nNo audio device is active, so nothing can be heard. Check Windows' sound output.";
        if (!probe) return text;
        var (loaded, total, failed) = Strata.Game.Audio.AudioDiagnostics.ProbeFiles();
        text += $"\nSound files: {loaded}/{total} loaded.";
        if (failed.Count > 0)
            text += $" Missing: {string.Join(", ", failed.Take(4))}{(failed.Count > 4 ? "…" : "")}. Open the project in the Godot editor once so the sounds are imported.";
        return text;
    }

    /// <summary>"125%", or "150% (effective 125%)" when the window is too small for the chosen scale.</summary>
    private static string UiScaleText(double v)
    {
        float effective = DisplaySettings.Effective((float)v, (Engine.GetMainLoop() as SceneTree)?.Root.Size ?? Vector2I.Zero);
        return effective < v - 0.001 ? $"{v * 100:0}% (effective {effective * 100:0}%)" : $"{v * 100:0}%";
    }

    private void Interface(VBoxContainer box)
    {
        box.AddChild(Header("Interface"));
        box.AddChild(MenuKit.Row("UI scale", MenuKit.Slider(0.75, 2, 0.05, S.UiScale, v => { S.UiScale = (float)v; Changed(); }, UiScaleText),
            "The interface never grows past what fits a 1024×600 layout in the window; a smaller effective scale is shown."));
        box.AddChild(MenuKit.Row("Show target name", MenuKit.Toggle(S.ShowTargetName, v => { S.ShowTargetName = v; Changed(); }),
            "Names the block or object under the crosshair."));
        box.AddChild(MenuKit.Row("Show FPS", MenuKit.Toggle(S.ShowFps, v => { S.ShowFps = v; Changed(); })));
        box.AddChild(MenuKit.Row("Minimap", MenuKit.Toggle(S.ShowMinimap, v => { S.ShowMinimap = v; Changed(); }), "The discovered map around you, top right. M opens the full map."));
        box.AddChild(MenuKit.Row("Cursor colour", MenuKit.ColorField(S.CursorColor, h => { S.CursorColor = h; Changed(); })));
    }
}
