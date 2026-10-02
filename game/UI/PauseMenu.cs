using System;
using Godot;
using Strata.Game.Bootstrap;
using Strata.Game.Menu;
using Strata.Game.World;

namespace Strata.Game.UI;

/// <summary>
/// Esc in game: pauses the world and offers Resume, Settings, Save &amp; Exit (to the title screen) and Save &amp; Quit.
/// Other screens (inventory, dialogs, console) close on Esc first.
/// </summary>
public sealed partial class PauseMenu : CanvasLayer
{
    private const string Gate = "pause";
    private GameServices _services = null!;
    private WorldRuntime _runtime = null!;
    private Control _root = null!;
    private Action _exitToTitle = null!;

    public bool IsOpen => _root.Visible;

    public void Initialize(GameServices services, WorldRuntime runtime, Action exitToTitle)
    {
        _services = services;
        _runtime = runtime;
        _exitToTitle = exitToTitle;
        Name = "PauseMenu";
        Layer = 45;
        _root = new ColorRect { Color = new Color(0.02f, 0.03f, 0.06f, 0.6f), Visible = false };
        _root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_root);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (!e.IsActionPressed("pause") || InputGate.TextEntry) return;
        if (!IsOpen && InputGate.MouseFree) return; // an inventory, dialog or the map closes itself on Esc
        GetViewport().SetInputAsHandled();
        SetOpen(!IsOpen);
    }

    public void SetOpen(bool open)
    {
        _root.Visible = open;
        _runtime.Simulating = !open;
        InputGate.SetOpen(Gate, open, GateKind.Modal);
        if (open) ShowMain();
    }

    private void ShowMain()
    {
        foreach (var child in _root.GetChildren()) child.QueueFree();
        var column = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        column.AddThemeConstantOverride("separation", 6);
        column.AddChild(MenuKit.Title("Paused", 54));
        column.AddChild(MenuKit.Spacer(24));
        column.AddChild(MenuKit.Big("Resume", () => SetOpen(false), 32));
        column.AddChild(MenuKit.Big("Settings", ShowSettings, 32));
        column.AddChild(MenuKit.Big("Save & Exit to Title", () =>
        {
            SetOpen(false);
            _exitToTitle();
        }, 32));
        column.AddChild(MenuKit.Big("Save & Quit Game", () =>
        {
            SetOpen(false);
            _runtime.Game.SaveAll();
            GetTree().Quit();
        }, 32));
        _root.AddChild(MenuKit.Centered(column));
    }

    private void ShowSettings()
    {
        foreach (var child in _root.GetChildren()) child.QueueFree();
        var screen = new SettingsScreen();
        screen.Initialize(_services, GetViewport(), ShowMain);
        _root.AddChild(MenuKit.Centered(screen));
    }
}
