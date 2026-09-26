using System;
using System.Threading.Tasks;
using Godot;
using Strata.Core.Gameplay.Player;
using Strata.Core.World;
using Strata.Game.Bootstrap;
using Strata.Game.Menu;
using Strata.Game.UI;

namespace Strata.Game.Testing;

/// <summary>
/// <c>--menu-tour[=DIR]</c>: drives the title screen end to end — create a character and a world, open the settings,
/// play, open the pause menu, save and exit back to the title — taking screenshots into DIR when rendering, and
/// printing <c>MENU PASS</c> (or <c>MENU FAIL</c> with the error) before quitting. Headless runs are the CI check.
/// </summary>
public sealed partial class MenuTour : Node
{
    private GameRoot _root = null!;
    private string? _dir;

    public void Start(GameRoot root, string? shotDirectory)
    {
        _root = root;
        _dir = shotDirectory;
        Name = "MenuTour";
        Run();
    }

    private async void Run()
    {
        try
        {
            var menu = await WaitFor(() => _root.GetNodeOrNull<MainMenu>("MainMenu"), 10);
            await Shot("m01_title", 1.5);
            menu.Open("create_character");
            await Shot("m02_create_character", 2.5);
            var look = new Appearance { Body = "female", HairStyle = "ponytail", HairColor = "#c89050", ShirtColor = "#c04040", PantsColor = "#303038" };
            menu.CreateAndSelectCharacter("Tour Hero", look);
            menu.Open("characters");
            await Shot("m03_characters", 2.5);
            menu.Open("create_world");
            await Shot("m04_create_world", 1.0);
            var dir = menu.CreateWorldNow(new WorldSettings { Name = "Tour World", Seed = 4242, Size = WorldSizePreset.Tiny, Difficulty = Difficulty.Expert, Evil = EvilKind.Sanguine });
            menu.Open("worlds");
            await Shot("m05_worlds", 1.0);
            menu.Open("settings");
            await Shot("m06_settings", 1.0);
            var tabs = FindTabs(menu);
            if (tabs != null)
                for (int i = 1; i < tabs.GetTabCount(); i++)
                {
                    tabs.CurrentTab = i;
                    await Shot($"m07_settings_{tabs.GetTabTitle(i).ToLowerInvariant()}", 0.6);
                }
            menu.Play(dir, "Tour Hero");
            await WaitFor(() => _root.World != null && _root.GetNodeOrNull("WorldHost/World/Player") is Player.PlayerController { Frozen: false } ? _root : null, 300);
            await Shot("m08_in_game", 3.0);
            var pause = _root.GetNode<PauseMenu>("WorldHost/PauseMenu");
            pause.SetOpen(true);
            await Shot("m09_paused", 1.0);
            pause.SetOpen(false);
            _root.ExitToMenu();
            await WaitFor(() => _root.GetNodeOrNull<MainMenu>("MainMenu"), 10);
            await Shot("m10_back_to_title", 1.0);
            if (!System.IO.File.Exists(System.IO.Path.Combine(dir, "world.json"))) throw new Exception("the world was not saved");
            GD.Print("MENU PASS");
            GetTree().Quit(0);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"MENU FAIL {ex}");
            GetTree().Quit(1);
        }
    }

    private static TabContainer? FindTabs(Node node)
    {
        if (node is TabContainer t) return t;
        foreach (var child in node.GetChildren())
            if (FindTabs(child) is { } found) return found;
        return null;
    }

    private async Task<T> WaitFor<T>(Func<T?> probe, double timeoutSeconds) where T : class
    {
        double waited = 0;
        while (true)
        {
            if (probe() is { } value) return value;
            if (waited > timeoutSeconds) throw new TimeoutException($"waited {timeoutSeconds}s");
            await ToSignal(GetTree().CreateTimer(0.1), SceneTreeTimer.SignalName.Timeout);
            waited += 0.1;
        }
    }

    private async Task Shot(string name, double settle)
    {
        await ToSignal(GetTree().CreateTimer(settle), SceneTreeTimer.SignalName.Timeout);
        GD.Print($"[menu-tour] {name}");
        if (_dir == null || DisplayServer.GetName() == "headless") return;
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var image = GetViewport().GetTexture().GetImage();
        System.IO.Directory.CreateDirectory(_dir);
        image.SavePng(System.IO.Path.Combine(_dir, name + ".png"));
    }
}
