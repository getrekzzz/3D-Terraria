using System.Numerics;
using Strata.Core.Common;
using Strata.Core.Lighting;
using Strata.Core.Meshing;
using Strata.Core.Tests.Support;
using Strata.Core.Voxels;

namespace Strata.Core.Tests.Meshing;

public class HeightfieldSmoothingTests
{
    private static readonly RenderTables Tables = new(TestPaths.Content, _ => 0);

    private static ChunkMeshResult Mesh(VoxelWorld world, Int3 chunk)
    {
        var nb = Neighborhood.Capture(world, chunk) ?? throw new InvalidOperationException("missing neighbours");
        return ChunkMesher.Build(nb, Tables, (_, _) => 10_000, Array.Empty<LightSource>());
    }

    /// <summary>An air world of natural stone that is solid below <paramref name="height"/>(x, z) in every column.</summary>
    private static VoxelWorld Terrain(Func<int, int, int> height, int chunks = 3, Action<EditBatch, ushort>? extra = null)
    {
        var world = TestWorlds.Filled("air", chunks);
        ushort stone = world.Content.BlockId("stone");
        var batch = world.BeginEdit();
        int size = chunks * ChunkMath.Size;
        for (int x = 0; x < size; x++)
            for (int z = 0; z < size; z++)
                for (int y = 0; y < height(x, z); y++) batch.SetBlock(x, y, z, stone);
        extra?.Invoke(batch, stone);
        batch.Commit();
        return world;
    }

    /// <summary>Up-facing vertex positions of a chunk's terrain, in world cells.</summary>
    private static List<Vector3> UpVertices(ChunkMeshResult mesh, Int3 chunk)
    {
        var origin = ChunkMath.ChunkOrigin(chunk);
        var list = new List<Vector3>();
        for (int i = 0; i < mesh.Opaque.VertexCount; i++)
            if (mesh.Opaque.Normals[i].Y > 0.5f)
                list.Add(mesh.Opaque.Positions[i] / Units.CellSize + new Vector3(origin.X, origin.Y, origin.Z));
        return list;
    }

    /// <summary>Max and RMS distance (cells) of vertex heights from their least-squares line y = a + b·x.</summary>
    private static (double Max, double Rms) RampDeviation(List<Vector3> v)
    {
        double n = v.Count, sx = v.Sum(p => p.X), sy = v.Sum(p => p.Y), sxx = v.Sum(p => (double)p.X * p.X), sxy = v.Sum(p => (double)p.X * p.Y);
        double b = (n * sxy - sx * sy) / (n * sxx - sx * sx), a = (sy - b * sx) / n;
        var residuals = v.Select(p => Math.Abs(p.Y - (a + b * p.X))).ToList();
        return (residuals.Max(), Math.Sqrt(residuals.Sum(r => r * r) / n));
    }

    /// <summary>Every free vertex of the last mesh stays in its dual cell and within MaxDrift of its centroid.</summary>
    private static void AssertDriftBound()
    {
        var s = SurfaceNetsMesher.ThreadScratch!;
        const float eps = 1e-4f;
        int checkedCount = 0;
        for (int y = Neighborhood.Margin - 1; y < Neighborhood.Margin + ChunkMath.Size; y++)
            for (int z = Neighborhood.Margin - 1; z < Neighborhood.Margin + ChunkMath.Size; z++)
                for (int x = Neighborhood.Margin - 1; x < Neighborhood.Margin + ChunkMath.Size; x++)
                {
                    int di = SurfaceNetsMesher.DI(x, y, z);
                    if (s.State[di] != SurfaceNetsMesher.Free) continue;
                    var p = s.Pos[di];
                    var d = Vector3.Abs(p - s.Initial[di]);
                    Assert.True(MathF.Max(d.X, MathF.Max(d.Y, d.Z)) <= SurfaceNetsMesher.MaxDrift + eps, $"vertex ({x},{y},{z}) drifted {d}");
                    var min = new Vector3(x + 0.5f, y + 0.5f, z + 0.5f);
                    Assert.True(p.X >= min.X - eps && p.Y >= min.Y - eps && p.Z >= min.Z - eps
                        && p.X <= min.X + 1 + eps && p.Y <= min.Y + 1 + eps && p.Z <= min.Z + 1 + eps, $"vertex ({x},{y},{z}) left its dual cell: {p}");
                    checkedCount++;
                }
        Assert.True(checkedCount > 100);
    }

    [Fact]
    public void GentleRampHasNoStairs()
    {
        var chunk = new Int3(1, 1, 1);
        var mesh = Mesh(Terrain((x, _) => 40 + x / 6), chunk);
        AssertDriftBound();
        var (max, rms) = RampDeviation(UpVertices(mesh, chunk));
        Assert.True(max < 0.15 && rms < 0.06, $"1-in-6 ramp deviates from its plane: max {max:F3}, RMS {rms:F3} cells");
    }

    [Fact]
    public void VeryGentleRampHasNoStairs()
    {
        var chunk = new Int3(1, 1, 1);
        var (max, rms) = RampDeviation(UpVertices(Mesh(Terrain((x, _) => 40 + x / 12), chunk), chunk));
        Assert.True(max < 0.25, $"1-in-12 ramp deviates from its plane: max {max:F3}, RMS {rms:F3} cells");
    }

    [Fact]
    public void DiagonalRampHasNoStairs()
    {
        // Terraces running diagonally have corner vertices (one opaque sample below, or three above): smoothed too.
        var chunk = new Int3(1, 1, 1);
        var up = UpVertices(Mesh(Terrain((x, z) => 40 + (x + 2 * z) / 9), chunk), chunk);
        var (max, rms) = RampDeviation(up.Select(p => new Vector3(p.X + 2 * p.Z, p.Y, 0)).ToList());
        Assert.True(max < 0.15 && rms < 0.06, $"diagonal ramp deviates from its plane: max {max:F3}, RMS {rms:F3} cells");
    }

    [Fact]
    public void SteepRampStaysInDualCells()
    {
        Mesh(Terrain((x, _) => Math.Clamp(x, 1, 95)), new Int3(1, 1, 1));
        AssertDriftBound();
        Mesh(Terrain((x, z) => Math.Clamp(16 + (x + z) / 2, 1, 95)), new Int3(1, 1, 1));
        AssertDriftBound();
    }

    [Fact]
    public void ThinLedgesKeepTheirHeight()
    {
        // Flat ground at 40 and a natural 3×3 slab floating at 42, one cell thick: the ground columns around it are
        // within a gentle step, but the slab top must not be pulled down towards them.
        var chunk = new Int3(1, 1, 1);
        var mesh = Mesh(Terrain((_, _) => 40, extra: (b, stone) =>
        {
            for (int x = 47; x < 50; x++)
                for (int z = 47; z < 50; z++) b.SetBlock(x, 42, z, stone);
        }), chunk);
        var top = UpVertices(mesh, chunk).Where(p => p.Y > 42f && MathF.Abs(p.X - 48.5f) < 1.1f && MathF.Abs(p.Z - 48.5f) < 1.1f).ToList();
        Assert.NotEmpty(top);
        // Relaxation alone leaves the slab's middle at ~42.86; pulled towards the ground it would sit on the 42.55 drift limit.
        Assert.All(top, p => Assert.True(p.Y > 42.75f, $"ledge top pulled down to {p.Y}"));
    }

    /// <summary>States and positions (region cells) of the dual vertices on the slice <paramref name="index"/> along an axis.</summary>
    private static List<(byte State, Vector3 Pos)> DualSlice(VoxelWorld world, Int3 chunk, int axis, int index)
    {
        Mesh(world, chunk);
        var s = SurfaceNetsMesher.ThreadScratch!;
        var slice = new List<(byte, Vector3)>();
        for (int u = Neighborhood.Margin - 1; u < Neighborhood.Margin + ChunkMath.Size; u++)
            for (int v = Neighborhood.Margin - 1; v < Neighborhood.Margin + ChunkMath.Size; v++)
            {
                int di = axis == 0 ? SurfaceNetsMesher.DI(index, u, v) : SurfaceNetsMesher.DI(u, index, v);
                slice.Add((s.State[di], s.Pos[di]));
            }
        return slice;
    }

    /// <summary>
    /// The dual vertices on the seam between <paramref name="a"/> and its next chunk along the axis come out the same
    /// in both chunks' meshes (the last dual layer of one region is the first of the next, 32 cells over).
    /// </summary>
    private static int AssertSeam(VoxelWorld world, Int3 a, int axis)
    {
        var step = axis == 0 ? new Int3(1, 0, 0) : new Int3(0, 1, 0);
        var shift = axis == 0 ? Vector3.UnitX * ChunkMath.Size : Vector3.UnitY * ChunkMath.Size;
        var mine = DualSlice(world, a, axis, Neighborhood.Margin + ChunkMath.Size - 1);
        var theirs = DualSlice(world, a + step, axis, Neighborhood.Margin - 1);
        int free = 0;
        for (int i = 0; i < mine.Count; i++)
        {
            Assert.Equal(mine[i].State, theirs[i].State);
            if (mine[i].State == 0) continue;
            float d = Vector3.Distance(mine[i].Pos - shift, theirs[i].Pos);
            Assert.True(d < 1e-3f, $"seam vertex differs by {d} cells between {a} and {a + step}: {mine[i].Pos - shift} vs {theirs[i].Pos}");
            if (mine[i].State == SurfaceNetsMesher.Free) free++;
        }
        return free;
    }

    [Fact]
    public void SmoothedRampsMatchAcrossVerticalAndHorizontalBorders()
    {
        // A diagonal ramp (1 in 6 along each axis) crossing the x = 64 plane and the y = 64 plane between chunk layers.
        var world = Terrain((x, z) => 52 + (x + z) / 6, chunks: 4);
        Assert.True(AssertSeam(world, new Int3(1, 2, 1), axis: 0) > 20);
        Assert.True(AssertSeam(world, new Int3(1, 1, 1), axis: 1) > 20);
        Assert.True(AssertSeam(world, new Int3(0, 1, 1), axis: 1) > 20);
    }

    [Fact]
    public void GeneratedTerrainMatchesAcrossHorizontalBorders()
    {
        var gen = new Strata.Core.WorldGen.WorldGenerator(TestPaths.Content, new World.WorldSettings { Seed = 1234, Size = World.WorldSizePreset.Tiny });
        var world = new VoxelWorld(TestPaths.Content, gen.Plan.Size, TestPaths.Content.BlockId("abyssal_rock"));
        var a = ChunkMath.CellToChunk(gen.Plan.Spawn);
        for (int dx = -1; dx <= 1; dx++)
            for (int dy = -2; dy <= 2; dy++)
                for (int dz = -1; dz <= 1; dz++)
                {
                    var c = a + new Int3(dx, dy, dz);
                    if (world.ChunkInBounds(c)) world.AddChunk(new Chunk(c, gen.Generate(c).Data));
                }
        int free = AssertSeam(world, a + new Int3(0, -1, 0), axis: 1) + AssertSeam(world, a, axis: 1);
        Assert.True(free > 0, "the surface crosses no horizontal chunk border near spawn");
    }

    [Fact]
    public void ChunkMeshesTheSameAloneAsAfterItsNeighbours()
    {
        var world = Terrain((x, z) => 40 + (x + 2 * z) / 9);
        var alone = Mesh(world, new Int3(1, 1, 1)).Opaque.Positions.ToArray();
        Mesh(world, new Int3(0, 1, 1));
        Mesh(world, new Int3(1, 1, 2));
        var again = Mesh(world, new Int3(1, 1, 1)).Opaque.Positions.ToArray();
        Assert.Equal(alone, again);
    }
}
