using System.Diagnostics;
using System.Numerics;
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
    public string Description => "Benchmark lighting+meshing of generated chunks around spawn (--seed N --depth K --roughness)";

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
        bool roughness = context.Options.Flag("roughness");
        double roughSq = 0;
        int roughN = 0;
        for (int dy = -1; dy <= 1; dy++)
            for (int dz = -2; dz <= 2; dz++)
                for (int dx = -2; dx <= 2; dx++)
                {
                    var nb = Neighborhood.Capture(world, center + new Int3(dx, dy, dz))!;
                    var sw = Stopwatch.StartNew();
                    var result = ChunkMesher.Build(nb, tables, sky.SkyTop, Array.Empty<LightSource>(), sky.ShadeTop);
                    times.Add(sw.Elapsed.TotalMilliseconds);
                    verts += result.Opaque.VertexCount + result.Transparent.VertexCount + result.Liquid.VertexCount;
                    tris += result.Opaque.TriangleCount + result.Transparent.TriangleCount + result.Liquid.TriangleCount;
                    if (roughness) Roughness(result.Opaque, ref roughSq, ref roughN);
                }
        times.Sort();
        Console.WriteLine($"{times.Count} chunks: median {times[times.Count / 2]:F2} ms, p90 {times[(int)(times.Count * 0.9)]:F2} ms, max {times[^1]:F2} ms");
        Console.WriteLine($"avg {verts / times.Count} vertices, {tris / times.Count} triangles per chunk");
        if (roughness)
            Console.WriteLine($"roughness: RMS {Math.Sqrt(roughSq / Math.Max(1, roughN)) * 100:F2} cm over {roughN} up-facing vertices (3x3 local plane fit)");
        return 0;
    }

    /// <summary>
    /// Ground ripple metric: for each up-facing vertex with up-facing neighbours in all 8 neighbouring dual columns
    /// (the one at dual height ±1 closest in height), the distance of its height from the least-squares plane through those 9 vertices.
    /// </summary>
    private static void Roughness(MeshSurface surface, ref double sumSq, ref int count)
    {
        var up = new Dictionary<(int, int, int), Vector3>();
        for (int i = 0; i < surface.VertexCount; i++)
        {
            if (surface.Normals[i].Y < 0.7f) continue;
            var p = surface.Positions[i];
            up[(Dual(p.X), Dual(p.Y), Dual(p.Z))] = p;
        }
        Span<double> m = stackalloc double[9];
        foreach (var ((x, y, z), p) in up)
        {
            // Normal equations of y = a + b·dx + c·dz over the 3x3 neighbourhood (positions relative to p).
            m.Clear();
            bool complete = true;
            for (int dz = -1; dz <= 1 && complete; dz++)
                for (int dx = -1; dx <= 1 && complete; dx++)
                {
                    if (!Nearest(up, x + dx, y, z + dz, p.Y, out var q)) { complete = false; break; }
                    double u = q.X - p.X, v = q.Z - p.Z, h = q.Y - p.Y;
                    m[0] += 1; m[1] += u; m[2] += v; m[3] += u * u; m[4] += u * v; m[5] += v * v;
                    m[6] += h; m[7] += u * h; m[8] += v * h;
                }
            if (!complete) continue;
            var a = new Matrix4x4((float)m[0], (float)m[1], (float)m[2], 0, (float)m[1], (float)m[3], (float)m[4], 0,
                (float)m[2], (float)m[4], (float)m[5], 0, 0, 0, 0, 1);
            if (!Matrix4x4.Invert(a, out var inv)) continue;
            double offset = inv.M11 * m[6] + inv.M12 * m[7] + inv.M13 * m[8]; // fitted plane height at p, relative to p
            sumSq += offset * offset;
            count++;
        }
    }

    private static int Dual(float metres) => (int)MathF.Floor(metres / Units.CellSize - 0.5f);

    /// <summary>The up-facing vertex of dual column (x, z) at dual height y ± 1 closest in height to <paramref name="height"/>.</summary>
    private static bool Nearest(Dictionary<(int, int, int), Vector3> up, int x, int y, int z, float height, out Vector3 p)
    {
        p = default;
        bool found = false;
        for (int dy = -1; dy <= 1; dy++)
            if (up.TryGetValue((x, y + dy, z), out var q) && (!found || MathF.Abs(q.Y - height) < MathF.Abs(p.Y - height)))
            {
                p = q;
                found = true;
            }
        return found;
    }
}
