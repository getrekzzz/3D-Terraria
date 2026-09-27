using System.Diagnostics;
using Strata.Core.Common;
using Strata.Core.Content;
using Strata.Core.Diagnostics;
using Strata.Core.Voxels;
using Strata.Core.World;
using Strata.Core.WorldGen.Chunk;
using Strata.Core.WorldGen.Fields;
using Strata.Core.WorldGen.Passes;
using Strata.Core.WorldGen.Plan;

namespace Strata.Core.WorldGen;

/// <summary>
/// Builds the world plan (all passes' plan hooks, once) and generates chunks on demand
/// (all passes' chunk hooks). Thread-safe: <see cref="Generate"/> may run on many workers at once.
/// </summary>
public sealed class WorldGenerator
{
    public WorldGenerator(ContentDatabase content, WorldSettings settings, ILog? log = null)
    {
        Content = content;
        Log = log ?? NullLog.Instance;
        Palette = new BlockPalette(content);
        Plan = new WorldPlan(settings);
        Passes = PassRegistry.CreateAll();
        Terrain = new TerrainField(Plan);
        Biomes = new BiomeField(Plan, Terrain, content);
        Caves = new CaveField(Plan);

        var sw = Stopwatch.StartNew();
        var ctx = new PlanContext(this);
        foreach (var pass in Passes) pass.Plan(ctx);
        Plan.RaiseTerrainColumnTops(Terrain.SurfaceHeight);
        Plan.Hash = PlanHasher.Compute(Plan);
        Log.Info("worldgen", $"Plan built in {sw.ElapsedMilliseconds} ms (seed {settings.Seed}, {settings.Size}, hash {Plan.Hash}).");
    }

    public ContentDatabase Content { get; }
    public ILog Log { get; }
    public WorldPlan Plan { get; }
    public BlockPalette Palette { get; }
    public TerrainField Terrain { get; }
    public BiomeField Biomes { get; }
    public CaveField Caves { get; }
    public IReadOnlyList<GenPass> Passes { get; }

    /// <summary>When set, per-pass chunk timings are recorded here (profiling/reporting).</summary>
    public Metrics? ProfilePasses { get; set; }

    /// <summary>Runs all chunk passes and returns the padded working context (tests/debug tools).</summary>
    public ChunkGenContext GenerateContext(Int3 coord)
    {
        var ctx = new ChunkGenContext(this, coord);
        foreach (var pass in Passes) pass.Generate(ctx);
        return ctx;
    }

    public GeneratedChunk Generate(Int3 coord)
    {
        if (coord.Y < 0) return Uniform(coord, Palette.Bedrock);
        if (Plan.IsAboveContent(coord)) return Uniform(coord, Palette.Air);

        var ctx = new ChunkGenContext(this, coord);
        if (ProfilePasses != null)
        {
            foreach (var pass in Passes)
            {
                long t = Stopwatch.GetTimestamp();
                pass.Generate(ctx);
                ProfilePasses.Record("gen." + pass.Name, Stopwatch.GetElapsedTime(t).TotalMilliseconds);
            }
        }
        else
        {
            foreach (var pass in Passes) pass.Generate(ctx);
        }
        return Crop(ctx);
    }

    private static GeneratedChunk Uniform(Int3 coord, ushort block) =>
        new(coord, new ChunkData(block), Array.Empty<Voxels.TileObjects.TileObjectInstance>(),
            new ChunkNavSummary(block == 0 ? (ushort)0x7FFF : (ushort)0, 0, block == 0 ? ChunkMath.Volume : 0));

    private static GeneratedChunk Crop(ChunkGenContext ctx)
    {
        var blocks = new ushort[ChunkMath.Volume];
        byte[]? liquids = ctx.Liquids != null ? new byte[ChunkMath.Volume] : null;
        byte[]? shapes = ctx.Shapes != null ? new byte[ChunkMath.Volume] : null;
        byte[]? flags = ctx.Flags != null ? new byte[ChunkMath.Volume] : null;
        for (int y = 0; y < ChunkMath.Size; y++)
            for (int z = 0; z < ChunkMath.Size; z++)
            {
                int src = ChunkGenContext.Index(ChunkGenContext.Pad, y + ChunkGenContext.Pad, z + ChunkGenContext.Pad);
                int dst = ChunkMath.Index(0, y, z);
                Array.Copy(ctx.Blocks, src, blocks, dst, ChunkMath.Size);
                if (liquids != null) Array.Copy(ctx.Liquids!, src, liquids, dst, ChunkMath.Size);
                if (shapes != null) Array.Copy(ctx.Shapes!, src, shapes, dst, ChunkMath.Size);
                if (flags != null) Array.Copy(ctx.Flags!, src, flags, dst, ChunkMath.Size);
            }
        return new GeneratedChunk(ctx.Coord, ChunkData.Compact(blocks, shapes, liquids, flags), ctx.Objects, ctx.Nav);
    }
}
