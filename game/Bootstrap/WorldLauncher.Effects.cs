using Strata.Core.Session;
using Strata.Game.Items;
using Strata.Game.World;

namespace Strata.Game.Bootstrap;

public static partial class WorldLauncher
{
    /// <summary>Hit and block effects, dropped items, dynamic lights and lava glow.</summary>
    private static void AddWorldEffects(WorldRuntime runtime, GameServices services, GameWorld game, TileObjectRenderer objectRenderer, Entities.NpcRenderer creatures)
    {
        var effects = new Entities.CombatEffects();
        runtime.AddChild(effects);
        effects.Initialize(game);
        var blockEffects = new Entities.BlockEffects();
        runtime.AddChild(blockEffects);
        blockEffects.Initialize(game);
        var drops = new DroppedItemRenderer();
        runtime.AddChild(drops);
        drops.Initialize(game);
        var lights = new Rendering.DynamicLights();
        runtime.AddChild(lights);
        lights.Initialize(services.Graphics);
        lights.AddProvider((eye, radius) => Rendering.DynamicLights.PlacedLights(game, objectRenderer.Placement, eye, radius));
        lights.AddProvider(creatures.Glows);
        var lava = new Rendering.LavaEffects();
        runtime.AddChild(lava);
        lava.Initialize(runtime);
        lights.AddProvider(lava.HeatLights);
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
    }
}
