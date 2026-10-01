using Strata.Core.Session;
using Strata.Game.World;

namespace Strata.Game.Bootstrap;

public static partial class WorldLauncher
{
    /// <summary>The shared prop library, placed tile objects and their colliders.</summary>
    private static (Rendering.PropLibrary Props, TileObjectRenderer Objects) AddWorldObjects(WorldRuntime runtime)
    {
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
    }

    /// <summary>Renderers for creatures, town residents and projectiles.</summary>
    private static Entities.NpcRenderer AddCreatureRenderers(WorldRuntime runtime, GameWorld game, Rendering.PropLibrary props)
    {
        var creatures = new Entities.NpcRenderer();
        runtime.AddChild(creatures);
        creatures.Initialize(game, props);
        var residents = new Entities.TownRenderer();
        runtime.AddChild(residents);
        residents.Initialize(game, props);
        var projectiles = new Entities.ProjectileRenderer();
        runtime.AddChild(projectiles);
        projectiles.Initialize(game, props);
        return creatures;
    }
}
