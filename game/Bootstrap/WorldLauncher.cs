using System.Globalization;
using Godot;
using Strata.Core.Diagnostics;
using Strata.Core.Gameplay.Player;
using Strata.Core.Persistence;
using Strata.Core.Session;
using Strata.Core.Streaming;
using Strata.Core.World;
using Strata.Game.Debug;
using Strata.Game.Items;
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
    public InteractionController? Interaction { get; init; }
    public FreeCamera? FreeCamera { get; init; }
}

/// <summary>Creates or loads a world and assembles the scene nodes that present it.</summary>
public static class WorldLauncher
{
    public static LaunchedWorld Launch(Node parent, GameServices services, string name, long seed, WorldSizePreset size, bool forceNew)
    {
        var args = services.Args;
        var dir = ProjectSettings.GlobalizePath($"user://worlds/{name}");
        if (forceNew && System.IO.Directory.Exists(dir)) System.IO.Directory.Delete(dir, recursive: true);
        var store = new WorldSaveStore(dir);
        var session = store.Exists
            ? WorldSession.Load(services.Content, dir, services.Render, services.Log)
            : WorldSession.Create(services.Content, new WorldSettings { Name = name, Seed = seed, Size = size }, dir, services.Render, services.Log);
        services.Log.Info("boot", $"World '{name}' seed {session.Settings.Seed} ({session.Settings.Size}).");
        if (args.Get("hour") is { } hour) session.Clock.SetHour(float.Parse(hour, CultureInfo.InvariantCulture));

        int view = (int)args.Long("view", 6);
        var streaming = new StreamingSettings { MeshRadius = view, MeshDown = System.Math.Min(3, view), MeshUp = System.Math.Min(3, view) };
        var playerDir = ProjectSettings.GlobalizePath("user://players");
        var game = new GameWorld(session, streaming, System.Math.Max(1, System.Environment.ProcessorCount - 2), playerDir);
        var runtime = new WorldRuntime { Name = "World" };
        runtime.Initialize(services, game);
        parent.AddChild(runtime);

        var objectColliders = new ObjectColliders { Name = "ObjectColliders" };
        runtime.AddChild(objectColliders);
        objectColliders.Initialize(runtime);
        var props = new Rendering.PropLibrary();
        var objectRenderer = new TileObjectRenderer();
        runtime.AddChild(objectRenderer);
        objectRenderer.Initialize(runtime, props);
        var drops = new DroppedItemRenderer();
        runtime.AddChild(drops);
        drops.Initialize(game);

        bool freeCam = args.Flag("free-camera") || args.Get("eye") != null;
        if (freeCam) return new LaunchedWorld { Runtime = runtime, Props = props, Objects = objectRenderer, FreeCamera = AddFreeCamera(runtime, session, args) };

        var playerName = args.Get("player") ?? "Wanderer";
        if (args.Flag("fresh-player")) GameWorld.DeletePlayer(playerDir, playerName);
        var state = GameWorld.LoadOrCreatePlayer(playerDir, playerName, services.Content);
        var entity = game.AddPlayer(state, game.SpawnPositionFor(state));
        var player = new PlayerController();
        player.Initialize(runtime, entity, MovementConfig.Load(services.Data));
        runtime.AddChild(player);
        player.Rig.Camera.Current = true;
        runtime.Focus = player;
        game.PlayerRespawned += p =>
        {
            if (p == entity) player.Teleport(p.Position, resolveSpawn: true);
        };

        var avatar = new PlayerAvatar();
        player.AddChild(avatar);
        avatar.Initialize(player, game, props);

        var interaction = new InteractionController();
        interaction.Initialize(runtime, player);
        runtime.AddChild(interaction);
        var highlight = new TargetHighlight();
        runtime.AddChild(highlight);
        highlight.Initialize(game, interaction, entity);
        return new LaunchedWorld { Runtime = runtime, Props = props, Objects = objectRenderer, Player = player, Interaction = interaction };
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
