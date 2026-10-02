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
/// (--export-theme writes the code-built UI theme to assets/ui/strata_theme.tres and quits).
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
        Settings.UserSettings.Load();
        UiTheme.Install(); // before any UI node exists: global font, theme and glyph warm-up
        log.Info("boot", $"UI theme installed (glyph warm-up {UiTheme.WarmupMs:0.0} ms).");
        if (args.Flag("export-theme"))
        {
            var exported = UiTheme.Export();
            log.Info("boot", $"Exported the UI theme to {UiTheme.ThemePath}: {exported}.");
            GetTree().Quit(exported == Error.Ok ? 0 : 1);
            return;
        }

        var content = ContentLoader.Load(data);
        foreach (var issue in content.Report.Issues)
            log.Write(issue.Severity == Strata.Core.Content.IssueSeverity.Error ? LogLevel.Error : LogLevel.Warning, "content", issue.ToString());
        if (content.Report.HasErrors)
        {
            ShowFatal("Content failed validation — see the log for details.");
            return;
        }

        var layers = TextureLayerCatalog.Load(data);
        var grass = Strata.Core.Presentation.GrassConfig.Load(data);
        var grassProblems = grass.Validate(content);
        if (grassProblems.Count > 0)
        {
            foreach (var problem in grassProblems) log.Error("content", $"{Strata.Core.Presentation.GrassConfig.Path}: {problem}");
            ShowFatal("Grass settings failed validation — see the log for details.");
            return;
        }
        var useStyleProblems = Strata.Core.Presentation.UseStyleConfig.Load(data).Validate();
        if (useStyleProblems.Count > 0)
        {
            foreach (var problem in useStyleProblems) log.Error("content", $"{Strata.Core.Presentation.UseStyleConfig.Path}: {problem}");
            ShowFatal("Use style settings failed validation — see the log for details.");
            return;
        }
        var render = new RenderTables(content, layers.IndexOf, grass);
        InputSetup.Register(data);
        Settings.KeyBindings.Load();
        var graphics = Rendering.GraphicsSettings.Load(data, args, log);
        graphics.Apply(GetViewport());
        Services = new GameServices(log, data, content, layers, render, args, graphics);
        log.Info("boot", $"Graphics preset '{graphics.Name}'.");
        log.Info("boot", $"Content: {content.Blocks.Count} blocks, {content.Items.Count} items, {content.Recipes.Count} recipes.");
        Audio.AudioBuses.Setup();
        Audio.AudioDiagnostics.LogStartup(log, IsAutomated(args));
        Audio.Sfx.Attach(this);
        AddChild(new Audio.MusicDirector());
        var dynamicResolution = new Rendering.DynamicResolution();
        dynamicResolution.Initialize(graphics);
        AddChild(dynamicResolution);
        var user = Settings.UserSettings.Current;
        user.Changed += ApplyDisplay;
        if (!IsAutomated(args)) ApplyDisplay(user);
        GetTree().Root.SizeChanged += () => Settings.DisplaySettings.ApplyUiScale(GetTree().Root, Settings.UserSettings.Current);

        if (args.Flag("no-world")) return;
        if (!IsAutomated(args) && args.Get("world") == null)
        {
            ShowMenu();
            if (args.Get("menu-tour") is { } tour)
            {
                var runner = new Testing.MenuTour();
                AddChild(runner);
                runner.Start(this, tour == "true" ? null : tour);
            }
            return;
        }
        // Developer and test starts: a named world from the command line.
        var name = args.Get("world") ?? "dev";
        StartWorld(new LaunchOptions
        {
            WorldDirectory = ProjectSettings.GlobalizePath($"user://worlds/{name}"),
            NewWorld = new WorldSettings
            {
                Name = name, Seed = args.Long("seed", 1234),
                Size = System.Enum.Parse<WorldSizePreset>(args.Get("size") ?? "small", ignoreCase: true),
            },
            ForceNew = args.Flag("new"),
            PlayerName = args.Get("player") ?? "Wanderer",
            FreshPlayer = args.Flag("fresh-player"),
            FreeCamera = args.Flag("free-camera") || args.Get("eye") != null,
            ViewDistance = (int)args.Long("view", user.ViewDistance),
        });
    }

    /// <summary>Scripted runs (smoke tests, screenshots) skip the menu and keep the window as launched.</summary>
    private static bool IsAutomated(CommandLine args) =>
        args.Get("smoke") != null || args.Get("screenshot") != null || args.Get("exec") != null || args.Get("shots") != null;

    private void ApplyDisplay(Settings.UserSettings s)
    {
        Settings.DisplaySettings.Apply(s);
        Settings.DisplaySettings.ApplyUiScale(GetTree().Root, s);
    }

    /// <summary>The title screen; choosing a world there starts it.</summary>
    public void ShowMenu()
    {
        Audio.MusicDirector.Instance?.PlayFixed("title");
        var menu = new Menu.MainMenu();
        AddChild(menu);
        menu.Initialize(Services);
        menu.PlayRequested += request =>
        {
            menu.QueueFree();
            StartWorld(new LaunchOptions
            {
                WorldDirectory = request.WorldDirectory,
                PlayerName = request.PlayerName,
                ViewDistance = Settings.UserSettings.Current.ViewDistance,
            });
        };
    }

    /// <summary>Saves and closes the open world, then shows the title screen.</summary>
    public void ExitToMenu()
    {
        if (_worldHost == null) return;
        var host = _worldHost;
        _worldHost = null;
        World = null;
        Commands = null;
        CommandContext = null;
        InputGate.Reset();
        System.Runtime.GCSettings.LatencyMode = System.Runtime.GCLatencyMode.Interactive;
        RemoveChild(host);
        host.QueueFree(); // the world saves as it leaves the tree
        ShowMenu();
    }

    private Node? _worldHost;

    public LaunchedWorld StartWorld(LaunchOptions options)
    {
        _worldHost = new Node { Name = "WorldHost" };
        AddChild(_worldHost);
        var launched = WorldLauncher.Launch(_worldHost, Services, options);
        World = launched.Runtime;
        // While a world runs, the GC avoids blocking gen2 collections (background ones instead): fewer freezes for a
        // little more memory. The menu goes back to the default.
        System.Runtime.GCSettings.LatencyMode = System.Runtime.GCLatencyMode.SustainedLowLatency;
        Commands = BuiltinCommands.CreateRegistry();
        EngineCommands.Register(Commands, launched);
        CommandContext = GodotCommandHost.CreateContext(launched, Services.Log, Commands);
        var console = new DebugConsole();
        _worldHost.AddChild(console);
        console.Initialize(Commands, CommandContext);
        if (launched.Player != null)
        {
            var pause = new PauseMenu();
            _worldHost.AddChild(pause);
            pause.Initialize(Services, launched.Runtime, ExitToMenu);
            // A hardcore character's death ends the session: show the message, then return to the title screen.
            launched.Runtime.Game.PlayerLost += _ => GetTree().CreateTimer(6.0).Timeout += () =>
            {
                if (World == launched.Runtime) ExitToMenu();
            };
        }
        ScreenshotCapture? capture = null;
        if (Services.Args.Get("screenshot") is { } shot)
        {
            capture = new ScreenshotCapture { World = launched.Runtime, OutputPath = shot };
            _worldHost.AddChild(capture);
        }
        AddTourShots(launched);
        if (Services.Args.Get("smoke") is { } script)
        {
            // Scripts aim from the eye: first person makes the crosshair ray and the eye ray coincide.
            if (launched.Player != null) launched.Player.Rig.FirstPerson = true;
            StartScript(script);
        }
        else if (Services.Args.Get("exec") is { } exec)
        {
            // Inline startup commands separated by ';' (screenshots, manual testing); the game keeps running.
            var runner = new ScriptRunner { Name = "ExecRunner", QuitWhenDone = false };
            bool done = false;
            runner.Finished += _ => done = true;
            if (capture != null) capture.WaitFor = () => done;
            _worldHost.AddChild(runner);
            runner.Start("--exec", exec.Replace(';', '\n'), Commands, CommandContext!);
        }
        else if (launched.Player != null) Input.MouseMode = Input.MouseModeEnum.Captured;
        return launched;
    }

    /// <summary>The <c>screenshot &lt;name&gt;</c> command: waits for streaming to settle and saves into <c>--shots</c>.</summary>
    private void AddTourShots(LaunchedWorld launched)
    {
        var shots = new ScreenshotCapture { Name = "TourShots", World = launched.Runtime, QuitAfter = false, Armed = false };
        _worldHost!.AddChild(shots);
        string dir = Services.Args.Get("shots") ?? "user://shots";
        Commands!.Register(new CommandDef
        {
            Name = "screenshot", Usage = "screenshot <name> [settle seconds]",
            Help = "Saves <shots dir>/<name>.png once streaming is idle (wait with: wait_until screenshot.pending == 0).",
            Run = (_, a) =>
            {
                var file = a.Str(0, "name");
                if (file.IndexOfAny(new[] { '/', '\\', '.' }) >= 0) throw new CommandException("name must be a plain file name");
                shots.SettleSeconds = a.Count > 1 ? a.Float(1, "settle") : 1.5;
                shots.Request($"{dir.TrimEnd('/')}/{file}.png");
                return CommandResult.Success($"Screenshot {file} requested.");
            },
        });
        CommandContext!.AddProbe("screenshot.pending", () => shots.Armed ? 1 : 0);
        CommandContext.AddProbe("render.fps", () => Engine.GetFramesPerSecond());
        CommandContext.AddProbe("render.draw_calls", () => RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalDrawCallsInFrame));
        CommandContext.AddProbe("render.primitives", () => RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalPrimitivesInFrame));
        CommandContext.AddProbe("render.objects", () => RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalObjectsInFrame));
    }

    private void StartScript(string path)
    {
        if (!FileAccess.FileExists(path)) throw new System.IO.FileNotFoundException($"Smoke script not found: {path}");
        var runner = new ScriptRunner { Name = "ScriptRunner" };
        _worldHost!.AddChild(runner);
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
        if (World != null && @event is InputEventMouseButton { Pressed: true } && !InputGate.GameplayBlocked && Input.MouseMode != Input.MouseModeEnum.Captured)
            Input.MouseMode = Input.MouseModeEnum.Captured;
    }
}
