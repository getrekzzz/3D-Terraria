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
    public UI.InventoryScreen? Inventory { get; init; }
    public UI.Hud? Hud { get; init; }
    public UI.TownDialog? TownDialog { get; init; }
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

/// <summary>Creates or loads a world and assembles the scene nodes that present it.</summary>
public static class WorldLauncher
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

        var objectColliders = new ObjectColliders { Name = "ObjectColliders" };
        runtime.AddChild(objectColliders);
        objectColliders.Initialize(runtime);
        var props = new Rendering.PropLibrary();
        var objectRenderer = new TileObjectRenderer();
        runtime.AddChild(objectRenderer);
        objectRenderer.Initialize(runtime, props);
        game.Spawner.Config = Strata.Core.Gameplay.Entities.SpawnConfig.Load(services.Data);
        game.Housing.Config = Strata.Core.Gameplay.Housing.HousingConfig.Load(services.Data);
        game.Town.Config = Strata.Core.Gameplay.Town.TownConfig.Load(services.Data);
        game.Spread.Config = Strata.Core.Gameplay.Progression.SpreadConfig.Load(services.Data);
        var creatures = new Entities.NpcRenderer();
        runtime.AddChild(creatures);
        creatures.Initialize(game, props);
        var residents = new Entities.TownRenderer();
        runtime.AddChild(residents);
        residents.Initialize(game, props);
        var projectiles = new Entities.ProjectileRenderer();
        runtime.AddChild(projectiles);
        projectiles.Initialize(game, props);
        var effects = new Entities.CombatEffects();
        runtime.AddChild(effects);
        effects.Initialize(game);
        var blockEffects = new Entities.BlockEffects();
        runtime.AddChild(blockEffects);
        blockEffects.Initialize(game);
        var drops = new DroppedItemRenderer();
        runtime.AddChild(drops);
        drops.Initialize(game);

        if (o.FreeCamera) return new LaunchedWorld { Runtime = runtime, Props = props, Objects = objectRenderer, FreeCamera = AddFreeCamera(runtime, session, args) };

        var playerName = o.PlayerName;
        if (o.FreshPlayer) GameWorld.DeletePlayer(playerDir, playerName);
        var state = GameWorld.LoadOrCreatePlayer(playerDir, playerName, services.Content);
        var entity = game.AddPlayer(state, game.SpawnPositionFor(state));
        var player = new PlayerController();
        player.Initialize(runtime, entity, MovementConfig.Load(services.Data));
        runtime.AddChild(player);
        player.Rig.Camera.Current = true;
        runtime.Focus = player;
        game.Npcs.PlayerHurt += (p, _, knock) =>
        {
            if (p == entity) player.ApplyKnockback(new Vector3(knock.X, knock.Y, knock.Z));
        };
        game.PlayerRespawned += p =>
        {
            if (p == entity) player.Teleport(p.Position, resolveSpawn: true);
        };
        game.ItemUse.Recalled += (p, home) =>
        {
            if (p == entity) player.Teleport(home, resolveSpawn: true);
        };

        var sounds = new Audio.SoundEvents();
        runtime.AddChild(sounds);
        sounds.Initialize(game, entity);
        Audio.MusicDirector.Instance?.Follow(game, entity);
        var senses = new Rendering.PlayerSenses();
        runtime.AddChild(senses);
        senses.Initialize(game, entity);

        var avatar = new PlayerAvatar();
        player.AddChild(avatar);
        avatar.Initialize(player, game, props);
        var viewModel = new ViewModel();
        player.Rig.Camera.AddChild(viewModel);
        viewModel.Initialize(player, game, props);
        var grapple = new GrappleVisual();
        runtime.AddChild(grapple);
        grapple.Initialize(player);
        var trail = new Entities.SwingTrail();
        runtime.AddChild(trail);
        trail.Initialize(game);
        var carried = new PlayerLight();
        player.AddChild(carried);
        carried.Initialize(entity, services.Graphics);

        var interaction = new InteractionController();
        interaction.Initialize(runtime, player);
        runtime.AddChild(interaction);
        var highlight = new TargetHighlight();
        runtime.AddChild(highlight);
        highlight.Initialize(game, interaction, entity);

        var ui = new UI.UiContext(game, player, interaction);
        var hud = new UI.Hud();
        runtime.AddChild(hud);
        hud.Initialize(ui);
        var inventory = new UI.InventoryScreen();
        runtime.AddChild(inventory);
        inventory.Initialize(ui);
        var townDialog = new UI.TownDialog();
        runtime.AddChild(townDialog);
        townDialog.Initialize(ui);
        var perf = new UI.PerfOverlay();
        runtime.AddChild(perf);
        perf.Initialize(runtime, objectRenderer);
        return new LaunchedWorld { Runtime = runtime, Props = props, Objects = objectRenderer, Player = player, Interaction = interaction, Inventory = inventory, Hud = hud, TownDialog = townDialog };
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
