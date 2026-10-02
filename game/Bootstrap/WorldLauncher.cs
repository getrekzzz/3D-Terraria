using System.Globalization;
using Godot;
using Strata.Core.Diagnostics;
using Strata.Core.Persistence;
using Strata.Core.Session;
using Strata.Core.Streaming;
using Strata.Core.World;
using Strata.Game.Debug;
using Strata.Game.Player;
using Strata.Game.World;

namespace Strata.Game.Bootstrap;

/// <summary>Everything created for one open world: runtime, player body and controllers, presentation nodes.</summary>
public sealed class LaunchedWorld
{
    public required WorldRuntime Runtime { get; init; }
    public required Rendering.PropLibrary Props { get; init; }
    public required TileObjectRenderer Objects { get; init; }
    public PlayerController? Player { get; init; }

    /// <summary>The player's third-person body (null in free-camera worlds).</summary>
    public PlayerAvatar? Avatar { get; init; }

    public InteractionController? Interaction { get; init; }
    public FreeCamera? FreeCamera { get; init; }
    public UI.InventoryScreen? Inventory { get; init; }
    public UI.Hud? Hud { get; init; }
    public UI.TownDialog? TownDialog { get; init; }
    public UI.WorldMapScreen? WorldMap { get; init; }

    /// <summary>The item tooltip that follows the pointer.</summary>
    public UI.TooltipFollower? Tooltip { get; init; }

    /// <summary>The stack held on the cursor, drawn at the pointer.</summary>
    public UI.CursorItemLayer? CursorItem { get; init; }
}

/// <summary>What to open: a world directory (created from <see cref="NewWorld"/> when missing) and a character.</summary>
public sealed record LaunchOptions
{
    public required string WorldDirectory { get; init; }

    /// <summary>Settings for creating the world when the directory holds none (command-line starts).</summary>
    public WorldSettings? NewWorld { get; init; }

    /// <summary>Delete any existing world in the directory first (tests).</summary>
    public bool ForceNew { get; init; }

    public string PlayerName { get; init; } = "Wanderer";

    /// <summary>Start the character over from the starting kit (tests).</summary>
    public bool FreshPlayer { get; init; }

    public bool FreeCamera { get; init; }

    /// <summary>Streaming radius in chunks.</summary>
    public int ViewDistance { get; init; } = 5;
}

/// <summary>
/// Creates or loads a world and assembles the scene nodes that present it. The wiring is grouped by concern in
/// partials (Assets, Effects, Player, Ui); nodes are added in a fixed order, which also fixes their processing order.
/// </summary>
public static partial class WorldLauncher
{
    public static LaunchedWorld Launch(Node parent, GameServices services, LaunchOptions o)
    {
        var args = services.Args;
        var dir = o.WorldDirectory;
        if (o.ForceNew && System.IO.Directory.Exists(dir)) System.IO.Directory.Delete(dir, recursive: true);
        var store = new WorldSaveStore(dir);
        var session = store.Exists
            ? WorldSession.Load(services.Content, dir, services.Render, services.Log)
            : WorldSession.Create(services.Content, o.NewWorld ?? new WorldSettings { Name = System.IO.Path.GetFileName(dir), Seed = 1234 }, dir, services.Render, services.Log);
        services.Log.Info("boot", $"World '{session.Settings.Name}' seed {session.Settings.Seed} ({session.Settings.Size}, {session.Settings.Difficulty}).");
        if (args.Get("hour") is { } hour) session.Clock.SetHour(float.Parse(hour, CultureInfo.InvariantCulture));

        int view = System.Math.Clamp(o.ViewDistance, 2, 12);
        var streaming = new StreamingSettings { MeshRadius = view, MeshDown = System.Math.Min(3, view), MeshUp = System.Math.Min(3, view) };
        var playerDir = ProjectSettings.GlobalizePath("user://players");
        var game = new GameWorld(session, streaming, System.Math.Max(1, System.Environment.ProcessorCount - 2), playerDir);
        var runtime = new WorldRuntime { Name = "World" };
        runtime.Initialize(services, game);
        parent.AddChild(runtime);
        runtime.AddChild(new FrameProbe()); // processes last whatever its place: closes each frame for the spike log

        var (props, objectRenderer) = AddWorldObjects(runtime);
        LoadConfigs(game, services);
        var creatures = AddCreatureRenderers(runtime, game, props);
        AddPreloading(runtime, services, game, props, creatures);
        var combatEffects = AddWorldEffects(runtime, services, game, objectRenderer, creatures);

        if (o.FreeCamera) return new LaunchedWorld { Runtime = runtime, Props = props, Objects = objectRenderer, FreeCamera = AddFreeCamera(runtime, session, args) };

        var (entity, player) = AddPlayer(runtime, services, game, o, playerDir);
        AddPlayerEffects(runtime, game, entity);
        combatEffects.Follow(entity, player);
        var avatar = AddPlayerVisuals(runtime, services, game, props, entity, player);
        var interaction = AddInteraction(runtime, game, entity, player);
        var (hud, inventory, worldMap, townDialog, tooltip, cursorItem) = AddUi(runtime, game, player, interaction, objectRenderer);
        return new LaunchedWorld
        {
            Runtime = runtime, Props = props, Objects = objectRenderer, Player = player, Avatar = avatar, Interaction = interaction,
            Inventory = inventory, Hud = hud, TownDialog = townDialog, WorldMap = worldMap, Tooltip = tooltip, CursorItem = cursorItem,
        };
    }

    private static FreeCamera AddFreeCamera(WorldRuntime runtime, WorldSession session, CommandLine args)
    {
        var camera = new FreeCamera { Name = "FreeCamera" };
        runtime.AddChild(camera);
        var spawn = session.SpawnCell;
        var eye = new Vector3(spawn.X, spawn.Y + 14, spawn.Z + 40) * Strata.Core.Common.Units.CellSize;
        if (args.Get("eye") is { } eyeArg) eye = ParseVector(eyeArg);
        var target = new Vector3(spawn.X, spawn.Y + 4, spawn.Z) * Strata.Core.Common.Units.CellSize;
        if (args.Get("look") is { } lookArg) target = ParseVector(lookArg);
        camera.LookFrom(eye, target);
        camera.Current = true;
        runtime.Focus = camera;
        return camera;
    }

    public static Vector3 ParseVector(string s)
    {
        var p = s.Split(',');
        return new Vector3(float.Parse(p[0], CultureInfo.InvariantCulture), float.Parse(p[1], CultureInfo.InvariantCulture), float.Parse(p[2], CultureInfo.InvariantCulture));
    }
}
