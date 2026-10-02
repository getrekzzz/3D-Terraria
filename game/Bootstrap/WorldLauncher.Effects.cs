using Strata.Core.Session;
using Strata.Game.Items;
using Strata.Game.World;

namespace Strata.Game.Bootstrap;

public static partial class WorldLauncher
{
    /// <summary>Hit and block effects, dropped items, dynamic lights and lava glow.</summary>
    private static void AddWorldEffects(WorldRuntime runtime, GameServices services, GameWorld game, TileObjectRenderer objectRenderer, Entities.NpcRenderer creatures)
    {
        var kit = new Rendering.EffectKit();
        runtime.AddChild(kit);
        kit.Initialize(game.Session.World, services.Graphics);
        var flashes = new Rendering.FlashLights();
        runtime.AddChild(flashes);
        flashes.Initialize(services.Graphics);
        var gore = new Entities.GoreSystem();
        runtime.AddChild(gore);
        gore.Initialize(game.Session.World, services.Graphics);
        var effects = new Entities.CombatEffects();
        runtime.AddChild(effects);
        effects.Initialize(game, kit, flashes, gore);
        var blockEffects = new Entities.BlockEffects();
        runtime.AddChild(blockEffects);
        blockEffects.Initialize(game, kit);
        var cracks = new Rendering.BlockCracks();
        runtime.AddChild(cracks);
        cracks.Initialize(game);
        var trees = new TreeEffects();
        runtime.AddChild(trees);
        trees.Initialize(game, objectRenderer);
        var drops = new DroppedItemRenderer();
        runtime.AddChild(drops);
        drops.Initialize(game);
        var lights = new Rendering.DynamicLights();
        runtime.AddChild(lights);
        lights.Initialize(services.Graphics);
        lights.AddProvider((eye, radius) => lights.PlacedLights(game, objectRenderer.Placement, eye, radius));
        lights.AddProvider(creatures.Glows);
        var lava = new Rendering.LavaEffects();
        runtime.AddChild(lava);
        lava.Initialize(runtime);
        lights.AddProvider(lava.HeatLights);
        var weather = new Ambience.WeatherRenderer();
        runtime.AddChild(weather);
        weather.Initialize(runtime, services.Graphics, game.Weather.Config);
        runtime.Weather = weather;
        var ambience = new Ambience.AmbienceDirector();
        runtime.AddChild(ambience);
        ambience.Initialize(runtime, services.Graphics, Strata.Core.Presentation.AmbienceConfig.Load(services.Data));
        runtime.Ambience = ambience;
    }

    /// <summary>What follows the local player: sounds, music, buff senses (night vision, Spelunker) and the wire overlay.</summary>
    private static void AddPlayerEffects(WorldRuntime runtime, GameWorld game, PlayerEntity entity)
    {
        var sounds = new Audio.SoundEvents();
        runtime.AddChild(sounds);
        sounds.Initialize(game, entity);
        Audio.MusicDirector.Instance?.Follow(game, entity);
        var senses = new Rendering.PlayerSenses();
        runtime.AddChild(senses);
        senses.Initialize(game, entity);
        var wires = new Rendering.WireOverlay();
        runtime.AddChild(wires);
        wires.Initialize(game, entity);
        var fishing = new Audio.FishingSounds();
        runtime.AddChild(fishing);
        fishing.Initialize(game, entity);
    }
}
