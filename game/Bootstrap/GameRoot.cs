using System.Globalization;
using Godot;
using Strata.Core.Commands;
using Strata.Core.Content.Loading;
using Strata.Core.Diagnostics;
using Strata.Core.Meshing;
using Strata.Core.World;
using Strata.Game.Debug;
using Strata.Game.Testing;
using Strata.Game.UI;
using Strata.Game.World;

namespace Strata.Game.Bootstrap;

/// <summary>
/// Composition root. Loads content (refusing to start on validation errors), registers input and
/// launches a world. Command line (after "--"): --world=NAME --seed=N --size=tiny|small|medium --new
/// </summary>
public partial class GameRoot : Node
{
    public GameServices Services { get; private set; } = null!;
    public WorldRuntime? World { get; private set; }
    public CommandRegistry? Commands { get; private set; }
    public CommandContext? CommandContext { get; private set; }

    public override void _Ready()
    {
        try
        {
            Boot();
        }
        catch (System.Exception ex)
        {
            GD.PushError($"Fatal startup error: {ex}");
            ShowFatal($"Startup failed: {ex.Message}");
            if (CommandLine.FromOs().Get("screenshot") != null || CommandLine.FromOs().Get("smoke") != null) GetTree().Quit(1);
        }
    }

    private void Boot()
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        var log = new GodotLog();
        var data = new GodotDataSource();
        var args = CommandLine.FromOs();

        var content = ContentLoader.Load(data);
        foreach (var issue in content.Report.Issues)
            log.Write(issue.Severity == Strata.Core.Content.IssueSeverity.Error ? LogLevel.Error : LogLevel.Warning, "content", issue.ToString());
        if (content.Report.HasErrors)
        {
            ShowFatal("Content failed validation — see the log for details.");
            return;
        }

        var layers = TextureLayerCatalog.Load(data);
        var render = new RenderTables(content, layers.IndexOf);
        InputSetup.Register(data);
        Services = new GameServices(log, data, content, layers, render, args);
        log.Info("boot", $"Content: {content.Blocks.Count} blocks, {content.Items.Count} items, {content.Recipes.Count} recipes.");

        if (args.Flag("no-world")) return;
        StartWorld(args.Get("world") ?? "dev", args.Long("seed", 1234),
            System.Enum.Parse<WorldSizePreset>(args.Get("size") ?? "small", ignoreCase: true), args.Flag("new"));
    }

    public LaunchedWorld StartWorld(string name, long seed, WorldSizePreset size, bool forceNew)
    {
        var launched = WorldLauncher.Launch(this, Services, name, seed, size, forceNew);
        World = launched.Runtime;
        Commands = BuiltinCommands.CreateRegistry();
        CommandContext = GodotCommandHost.CreateContext(launched, Services.Log, Commands);
        var console = new DebugConsole();
        AddChild(console);
        console.Initialize(Commands, CommandContext);
        if (Services.Args.Get("screenshot") is { } shot)
            AddChild(new ScreenshotCapture { World = launched.Runtime, OutputPath = shot });
        if (Services.Args.Get("smoke") is { } script)
        {
            // Scripts aim from the eye: first person makes the crosshair ray and the eye ray coincide.
            if (launched.Player != null) launched.Player.Rig.FirstPerson = true;
            StartScript(script);
        }
        else if (launched.Player != null) Input.MouseMode = Input.MouseModeEnum.Captured;
        return launched;
    }

    private void StartScript(string path)
    {
        if (!FileAccess.FileExists(path)) throw new System.IO.FileNotFoundException($"Smoke script not found: {path}");
        var runner = new ScriptRunner { Name = "ScriptRunner" };
        AddChild(runner);
        runner.Start(path, FileAccess.GetFileAsString(path), Commands!, CommandContext!);
    }

    private void ShowFatal(string message)
    {
        var label = new Label { Text = message, Position = new Vector2(40, 40) };
        var layer = new CanvasLayer();
        layer.AddChild(label);
        AddChild(layer);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("pause"))
            InputGate.SetOpen("pause", !InputGate.IsOpen("pause"));
        else if (@event is InputEventMouseButton { Pressed: true } && !InputGate.GameplayBlocked && Input.MouseMode != Input.MouseModeEnum.Captured)
            Input.MouseMode = Input.MouseModeEnum.Captured;
    }
}
