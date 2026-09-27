using System;
using Godot;
using Strata.Core.Diagnostics;
using Strata.Core.Persistence;
using Strata.Game.Bootstrap;

namespace Strata.Game.Menu;

/// <summary>What the menu asks the game to start: a saved world with a saved character.</summary>
public sealed record LaunchRequest(string WorldDirectory, string PlayerName);

/// <summary>
/// The title screen and its flow, as in the reference game: Single Player → pick or create a character → pick or
/// create a world → play; Settings; Exit. Screens are swapped inside one layer over the animated backdrop.
/// </summary>
public sealed partial class MainMenu : CanvasLayer
{
    private GameServices _services = null!;
    private Control _host = null!;
    private CharacterEntry? _character;

    /// <summary>Directory holding one folder per world.</summary>
    public static string WorldsDirectory => ProjectSettings.GlobalizePath("user://worlds");

    /// <summary>Directory holding the character saves.</summary>
    public static string PlayersDirectory => ProjectSettings.GlobalizePath("user://players");

    public event Action<LaunchRequest>? PlayRequested;

    public void Initialize(GameServices services)
    {
        _services = services;
        Name = "MainMenu";
        Layer = 50;
        var background = new MenuBackground();
        background.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(background);
        _host = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        _host.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_host);
        var version = MenuKit.Text("v1.0 · 3D Terraria", 14, new Color(1, 1, 1, 0.6f));
        version.SetAnchorsPreset(Control.LayoutPreset.BottomLeft);
        version.Position = new Vector2(12, -28);
        AddChild(version);
        Input.MouseMode = Input.MouseModeEnum.Visible;
        ShowTitle();
    }

    /// <summary>Opens a screen by name (automated tours): title, characters, create_character, worlds, create_world, settings.</summary>
    public void Open(string screen)
    {
        switch (screen)
        {
            case "characters": ShowCharacters(); break;
            case "create_character": ShowCharacterCreate(); break;
            case "worlds": ShowWorlds(); break;
            case "create_world": ShowWorldCreate(); break;
            case "settings": ShowSettings(ShowTitle); break;
            default: ShowTitle(); break;
        }
    }

    /// <summary>Creates a character and selects it (automated tours use the same path as the Create button).</summary>
    public void CreateAndSelectCharacter(string name, Strata.Core.Gameplay.Player.Appearance look)
    {
        CreateCharacter(name, look, Strata.Core.Gameplay.Player.CharacterDifficulty.Classic);
        _character = SaveCatalog.Characters(PlayersDirectory).Find(c => c.Name == name);
    }

    /// <summary>Creates a world synchronously and returns its directory (automated tours).</summary>
    public string CreateWorldNow(Strata.Core.World.WorldSettings settings)
    {
        var dir = SaveCatalog.NewWorldDirectory(WorldsDirectory, settings.Name);
        Strata.Core.Session.WorldSession.Create(_services.Content, settings, dir, _services.Render, _services.Log);
        return dir;
    }

    /// <summary>Starts a world as the Play button does.</summary>
    public void Play(string worldDirectory, string playerName) => PlayRequested?.Invoke(new LaunchRequest(worldDirectory, playerName));

    private void Show(Control screen)
    {
        foreach (var child in _host.GetChildren()) child.QueueFree();
        _host.AddChild(screen);
    }

    private void ShowTitle()
    {
        var column = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        column.AddThemeConstantOverride("separation", 8);
        column.AddChild(MenuKit.Title("3D Terraria", 72));
        column.AddChild(MenuKit.Spacer(40));
        column.AddChild(MenuKit.Big("Single Player", ShowCharacters, 36));
        column.AddChild(MenuKit.Big("Settings", () => ShowSettings(ShowTitle), 36));
        column.AddChild(MenuKit.Big("Exit", () => GetTree().Quit(), 36));
        Show(MenuKit.Centered(column));
    }

    private void ShowSettings(Action back)
    {
        var screen = new SettingsScreen();
        screen.Initialize(_services, GetViewport(), back);
        Show(MenuKit.Centered(screen));
    }

    /// <summary>A yes/no question over the current screen (deleting saves).</summary>
    private void Confirm(string question, Action yes)
    {
        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.55f) };
        dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        var (panel, box) = MenuKit.Panel(new Vector2(460, 0), 16);
        var text = MenuKit.Text(question, 20);
        text.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        text.HorizontalAlignment = HorizontalAlignment.Center;
        box.AddChild(text);
        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        buttons.AddThemeConstantOverride("separation", 20);
        buttons.AddChild(MenuKit.Small("Yes", () =>
        {
            dim.QueueFree();
            yes();
        }));
        buttons.AddChild(MenuKit.Small("No", dim.QueueFree));
        box.AddChild(buttons);
        dim.AddChild(MenuKit.Centered(panel));
        _host.AddChild(dim);
    }

    /// <summary>A modal "please wait" message; returns a callback that removes it.</summary>
    private Action Busy(string message)
    {
        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.6f) };
        dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        dim.AddChild(MenuKit.Centered(MenuKit.Title(message, 36)));
        _host.AddChild(dim);
        return dim.QueueFree;
    }

    private void Error(string message, Exception ex)
    {
        _services.Log.Error("menu", message, ex);
        Confirm($"{message}\n{ex.Message}\n\nBack to the title screen?", ShowTitle);
    }
}
