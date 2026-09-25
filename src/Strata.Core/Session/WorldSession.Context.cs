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
        ctx.Events = Events;
        return ctx;
    }

    /// <summary>
    /// Replaces a loaded chunk with freshly generated content (voxels and tile objects), discarding edits.
    /// Returns the chunks that must be remeshed.
    /// </summary>
    public HashSet<Int3> Regenerate(Int3 coord)
    {
        var chunk = World.GetChunk(coord) ?? throw new InvalidOperationException($"Chunk {coord} is not loaded.");
        var generated = Generator.Generate(coord);
        foreach (var obj in World.TileObjects.InChunk(coord).ToList()) World.TileObjects.Remove(obj);
        chunk.Publish(generated.Data);
        chunk.Modified = true;
        foreach (var obj in generated.Objects) World.TileObjects.TryAdd(obj);
        var dirty = new HashSet<Int3>();
        for (int dy = -1; dy <= 1; dy++)
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    var c = coord + new Int3(dx, dy, dz);
                    if (World.HasChunk(c)) dirty.Add(c);
                }
        foreach (var change in Sky.Rebuild(coord.X, coord.Z)) AddSkyInfluence(dirty, change);
        return dirty;
    }

    /// <summary>
    /// Finds the nearest surface column (spiral search, step in cells) whose surface biome is <paramref name="biomeId"/>,
    /// preferring dry land (a beach rather than the sea floor) and falling back to any matching column.
    /// </summary>
    public Int3? FindSurfaceBiome(string biomeId, Int3 from, int step = 16, int maxRadius = 4096) =>
        FindSurfaceBiome(biomeId, from, step, maxRadius, dryOnly: true) ?? FindSurfaceBiome(biomeId, from, step, maxRadius, dryOnly: false);

    private Int3? FindSurfaceBiome(string biomeId, Int3 from, int step, int maxRadius, bool dryOnly)
    {
        if (!Content.Biomes.TryGet(biomeId, out var biome)) return null;
        int sea = Generator.Plan.Bands.SeaLevel;
        for (int r = 0; r <= maxRadius; r += step)
        {
            for (int dz = -r; dz <= r; dz += step)
                for (int dx = -r; dx <= r; dx += step)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != r) continue;
                    int x = from.X + dx, z = from.Z + dz;
                    if (x < 0 || z < 0 || x >= Settings.Dimensions.X || z >= Settings.Dimensions.Z) continue;
                    if (Generator.Biomes.SurfaceBiome(x, z) != biome.RuntimeId) continue;
                    float surface = Generator.Terrain.SurfaceHeight(x, z);
                    if (dryOnly && surface < sea + 1) continue;
                    return new Int3(x, (int)MathF.Ceiling(surface) + 1, z);
                }
        }
        return null;
    }
}
