using System.Diagnostics;
using Strata.Core.Common;
using Strata.Core.Content.Loading;
using Strata.Core.Lighting;
using Strata.Core.Meshing;
using Strata.Core.Voxels;
using Strata.Core.World;
using Strata.Core.WorldGen;

namespace Strata.Tools.Commands;

/// <summary>Generates terrain around spawn and times lighting + meshing per chunk.</summary>
public sealed class MeshBenchCommand : IToolCommand
{
    public string Name => "mesh-bench";
    public string Description => "Benchmark lighting+meshing of generated chunks around spawn (--seed N --depth K)";

    public int Run(ToolContext context)
    {
        var content = ContentLoader.Load(new FileSystemDataSource(context.ProjectRoot));
        var gen = new WorldGenerator(content, new WorldSettings { Seed = context.Options.Long("seed", 1234), Size = WorldSizePreset.Small });
        var world = new VoxelWorld(content, gen.Plan.Size, content.BlockId("abyssal_rock"));
        var tables = new RenderTables(content, _ => 0);
        var center = ChunkMath.CellToChunk(gen.Plan.Spawn) - new Int3(0, context.Options.Int("depth", 0), 0);
        var coords = new List<Int3>();
        for (int dy = -2; dy <= 2; dy++)
            for (int dz = -3; dz <= 3; dz++)
                for (int dx = -3; dx <= 3; dx++)
                    coords.Add(center + new Int3(dx, dy, dz));
        Parallel.ForEach(coords, c => world.AddChunk(new Chunk(c, gen.Generate(c).Data)));

        var sky = new SkyMap(world, tables, (x, z) => (int)gen.Terrain.SurfaceHeight(x, z) + 1, (cx, cz) => gen.Plan.ColumnTop[gen.Plan.ColumnIndex(cx, cz)] >> 5);
        for (int dz = -3; dz <= 3; dz++)
            for (int dx = -3; dx <= 3; dx++)
                sky.Rebuild(center.X + dx, center.Z + dz);

        var times = new List<double>();
        long verts = 0, tris = 0;
        for (int dy = -1; dy <= 1; dy++)
            for (int dz = -2; dz <= 2; dz++)
                for (int dx = -2; dx <= 2; dx++)
                {
                    var nb = Neighborhood.Capture(world, center + new Int3(dx, dy, dz))!;
                    var sw = Stopwatch.StartNew();
                    var result = ChunkMesher.Build(nb, tables, sky.SkyTop, Array.Empty<LightSource>());
                    times.Add(sw.Elapsed.TotalMilliseconds);
                    verts += result.Opaque.VertexCount + result.Transparent.VertexCount + result.Liquid.VertexCount;
                    tris += result.Opaque.TriangleCount + result.Transparent.TriangleCount + result.Liquid.TriangleCount;
                }
        times.Sort();
        Console.WriteLine($"{times.Count} chunks: median {times[times.Count / 2]:F2} ms, p90 {times[(int)(times.Count * 0.9)]:F2} ms, max {times[^1]:F2} ms");
        Console.WriteLine($"avg {verts / times.Count} vertices, {tris / times.Count} triangles per chunk");
        return 0;
    }
}
