using Strata.Core.Common;
using Strata.Core.Conditions;

namespace Strata.Core.Session;

public sealed partial class WorldSession
{
    /// <summary>
    /// Fills a condition context for a cell: biome, layer, depth below the local surface, time and world flags.
    /// Used by loot, spawning, crafting environment checks and shops.
    /// </summary>
    public ConditionContext ContextAt(Int3 cell, ConditionContext? reuse = null)
    {
        var ctx = reuse ?? new ConditionContext();
        float surface = Generator.Terrain.SurfaceHeight(cell.X, cell.Z);
        ctx.Biomes = new[] { Generator.Biomes.IdOf(Generator.Biomes.BiomeAt(cell.X, cell.Y, cell.Z, surface)) };
        ctx.Layer = Generator.Plan.Bands.LayerAt(cell.Y);
        ctx.Depth = surface - cell.Y;
        ctx.Hour = Clock.Hour;
        ctx.IsDay = Clock.IsDay;
        ctx.MoonPhase = Clock.MoonPhase;
        ctx.Flags = Flags;
        return ctx;
    }
}
