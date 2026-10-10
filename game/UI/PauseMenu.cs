using System;
using Godot;
using Strata.Game.Bootstrap;
using Strata.Game.Menu;
using Strata.Game.World;

namespace Strata.Game.UI;

/// <summary>
/// Esc in game (or the inventory's 'Settings' button, as in Terraria): pauses the world under a light dim and shows a
/// centred blue panel of gold-on-hover text buttons: Resume, Settings, Save &amp; Exit to Title and Save &amp; Quit Game.
/// Other screens (inventory, dialogs, console) close on Esc first; the inventory stays open beneath it. Pausing stops
/// the whole scene tree, not just the simulation tick: physics, the player, creatures' animation, particles, the
/// day/night sky, music and every sound in flight hold where they are and carry on from there on Resume. Only this menu
/// (and its button clicks) keeps running.
/// </summary>
public sealed partial class PauseMenu : CanvasLayer
{
    /// <summary>The input gate it holds while open (a modal).</summary>
    public const string Gate = "pause";
    private GameServices _services = null!;
    private WorldRuntime _runtime = null!;
    private Control _root = null!;
    private Action _exitToTitle = null!;

    public bool IsOpen => _root.Visible;

    /// <summary>
    /// True while something else keeps the world paused (the inventory with auto-pause), so Resume does not restart it.
    /// </summary>
    public Func<bool>? StayPaused { get; set; }

    public void Initialize(GameServices services, WorldRuntime runtime, Action exitToTitle)
    {
        _services = services;
        _runtime = runtime;
        _exitToTitle = exitToTitle;
        Name = "PauseMenu";
        Layer = 45;
        ProcessMode = ProcessModeEnum.Always;
        _root = new ColorRect { Color = new Color(0f, 0f, 0f, 0.45f), Visible = false };
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
        _runtime.Simulating = !open && StayPaused?.Invoke() != true;
        GetTree().Paused = open;
        InputGate.SetOpen(Gate, open, GateKind.Modal);
        if (open) ShowMain();
    }

    /// <summary>Never leave the tree paused behind (exiting the world frees this menu).</summary>
    public override void _ExitTree()
    {
        if (IsOpen) GetTree().Paused = false;
    }

    private void ShowMain()
    {
        foreach (var child in _root.GetChildren()) child.QueueFree();
        var (panel, box) = MenuKit.Panel(new Vector2(440, 0), 4);
        box.Alignment = BoxContainer.AlignmentMode.Center;
        box.AddChild(MenuKit.Title("Paused", 44));
        box.AddChild(MenuKit.Spacer(8));
        box.AddChild(MenuKit.Big("Resume", () => SetOpen(false), 30));
        box.AddChild(MenuKit.Big("Settings", ShowSettings, 30));
        box.AddChild(MenuKit.Big("Save & Exit to Title", () =>
        {
            SetOpen(false);
            _exitToTitle();
        }, 30));
        box.AddChild(MenuKit.Big("Save & Quit Game", () =>
        {
            SetOpen(false);
            _runtime.Game.SaveAll();
            GetTree().Quit();
        }, 30));
        _root.AddChild(MenuKit.Centered(panel));
    }

    private void ShowSettings()
    {
        foreach (var child in _root.GetChildren()) child.QueueFree();
        var screen = new SettingsScreen();
        screen.Initialize(_services, GetViewport(), ShowMain);
        _root.AddChild(MenuKit.Centered(screen));
    }
}
