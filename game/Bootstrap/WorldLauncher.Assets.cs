using Strata.Core.Session;
using Strata.Game.World;

namespace Strata.Game.Bootstrap;

public static partial class WorldLauncher
{
    /// <summary>The shared prop library, placed tile objects and their colliders.</summary>
    private static (Rendering.PropLibrary Props, TileObjectRenderer Objects) AddWorldObjects(WorldRuntime runtime)
    {
        runtime.AddChild(new Rendering.AssetPump { Name = "AssetPump" }); // threaded model loads (Rendering.AssetCache)
        var objectColliders = new ObjectColliders { Name = "ObjectColliders" };
        runtime.AddChild(objectColliders);
        objectColliders.Initialize(runtime);
        var props = new Rendering.PropLibrary();
        var objectRenderer = new TileObjectRenderer();
        runtime.AddChild(objectRenderer);
        objectRenderer.Initialize(runtime, props);
        return (props, objectRenderer);
    }

    /// <summary>Gameplay tuning loaded from data files.</summary>
    private static void LoadConfigs(GameWorld game, GameServices services)
    {
        game.Spawner.Config = Strata.Core.Gameplay.Entities.SpawnConfig.Load(services.Data);
        game.ItemUse.Timing = Strata.Core.Gameplay.Items.UseTiming.Load(services.Data);
        game.Housing.Config = Strata.Core.Gameplay.Housing.HousingConfig.Load(services.Data);
        game.Town.Config = Strata.Core.Gameplay.Town.TownConfig.Load(services.Data);
        game.Spread.Config = Strata.Core.Gameplay.Progression.SpreadConfig.Load(services.Data);
        game.Journal.Config = Strata.Core.Gameplay.Guide.BestiaryConfig.Load(services.Data);
        game.Weather.Config = Strata.Core.Gameplay.Weather.WeatherConfig.Load(services.Data);
        game.Banners.Config = Strata.Core.Gameplay.Banners.BannerConfig.Load(services.Data);
        game.Projectiles.Fishing = Strata.Core.Gameplay.Fishing.FishingConfig.Load(services.Data);
        game.Session.Seasons.Config = Strata.Core.Gameplay.Seasons.SeasonConfig.Load(services.Data);
        game.Session.Seasons.Enabled = () => Settings.UserSettings.Current.SeasonalEvents;
        // Scripted runs (smoke tests, screenshots) never follow the calendar: a goodie bag dropping in October would make
        // them depend on the date. Scripts that need a season set it with `season <id>`.
        if (services.Args.Get("smoke") != null) game.Session.Seasons.Override = "";
    }

    /// <summary>Renderers for creatures, town residents and projectiles.</summary>
    private static (Entities.NpcRenderer, Entities.ProjectileRenderer) AddCreatureRenderers(WorldRuntime runtime, GameWorld game, Rendering.PropLibrary props)
    {
        var creatures = new Entities.NpcRenderer();
        runtime.AddChild(creatures);
        creatures.Initialize(game, props);
        var residents = new Entities.TownRenderer();
        runtime.AddChild(residents);
        residents.Initialize(game, props);
        var hearts = new Entities.TownPetHearts();
        runtime.AddChild(hearts);
        hearts.Initialize(game);
        var projectiles = new Entities.ProjectileRenderer();
        runtime.AddChild(projectiles);
        projectiles.Initialize(game, props);
        return (creatures, projectiles);
    }

    /// <summary>Relevance-driven preloading with pool pre-warming, and the one-time shader warm-up.</summary>
    private static void AddPreloading(WorldRuntime runtime, GameServices services, GameWorld game, Rendering.PropLibrary props, Entities.NpcRenderer creatures)
    {
        var preloader = new Rendering.AssetPreloader();
        preloader.Initialize(game, creatures, services.Graphics.Name == "low");
        runtime.AddChild(preloader);
        var warmup = new Rendering.ShaderWarmup();
        warmup.Initialize(props);
        runtime.AddChild(warmup);
    }
}
