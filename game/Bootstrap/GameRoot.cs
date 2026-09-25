using System.Globalization;
using Godot;
using Strata.Core.Content.Loading;
using Strata.Core.Diagnostics;
using Strata.Core.Meshing;
using Strata.Core.Session;
using Strata.Core.Streaming;
using Strata.Core.World;
using Strata.Game.Debug;
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
            if (CommandLine.FromOs().Get("screenshot") != null || CommandLine.FromOs().Flag("smoke")) GetTree().Quit(1);
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

    public WorldRuntime StartWorld(string name, long seed, WorldSizePreset size, bool forceNew)
    {
        var dir = ProjectSettings.GlobalizePath($"user://worlds/{name}");
        var store = new Strata.Core.Persistence.WorldSaveStore(dir);
        if (forceNew && System.IO.Directory.Exists(dir)) System.IO.Directory.Delete(dir, recursive: true);
        var session = store.Exists
            ? WorldSession.Load(Services.Content, dir, Services.Render, Services.Log)
            : WorldSession.Create(Services.Content, new WorldSettings { Name = name, Seed = seed, Size = size }, dir, Services.Render, Services.Log);
        Services.Log.Info("boot", $"World '{name}' seed {session.Settings.Seed} ({session.Settings.Size}).");

        var runtime = new WorldRuntime { Name = "World" };
        int view = (int)Services.Args.Long("view", 6);
        runtime.Initialize(Services, session, new StreamingSettings { MeshRadius = view, MeshDown = System.Math.Min(3, view), MeshUp = System.Math.Min(3, view) });
        AddChild(runtime);
        World = runtime;

        var camera = new FreeCamera { Name = "FreeCamera" };
        runtime.AddChild(camera);
        var spawn = session.SpawnCell;
        var eye = new Vector3(spawn.X, spawn.Y + 14, spawn.Z + 40) * Strata.Core.Common.Units.CellSize;
        if (Services.Args.Get("eye") is { } eyeArg) eye = ParseVector(eyeArg);
        var target = new Vector3(spawn.X, spawn.Y + 4, spawn.Z) * Strata.Core.Common.Units.CellSize;
        if (Services.Args.Get("look") is { } lookArg) target = ParseVector(lookArg);
        camera.LookFrom(eye, target);
        camera.Current = true;
        runtime.Focus = camera;

        if (Services.Args.Get("screenshot") is { } shot)
        {
            if (Services.Args.Get("hour") is { } hour) session.Clock.SetHour(float.Parse(hour, CultureInfo.InvariantCulture));
            AddChild(new ScreenshotCapture { World = runtime, OutputPath = shot });
        }
        return runtime;
    }

    private static Vector3 ParseVector(string s)
    {
        var p = s.Split(',');
        return new Vector3(float.Parse(p[0], CultureInfo.InvariantCulture), float.Parse(p[1], CultureInfo.InvariantCulture), float.Parse(p[2], CultureInfo.InvariantCulture));
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
            Input.MouseMode = Input.MouseMode == Input.MouseModeEnum.Captured ? Input.MouseModeEnum.Visible : Input.MouseModeEnum.Captured;
        else if (@event is InputEventMouseButton { Pressed: true } && Input.MouseMode != Input.MouseModeEnum.Captured)
            Input.MouseMode = Input.MouseModeEnum.Captured;
    }
}
