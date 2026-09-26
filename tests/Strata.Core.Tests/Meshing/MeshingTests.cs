using System.Numerics;
using Strata.Core.Common;
using Strata.Core.Lighting;
using Strata.Core.Meshing;
using Strata.Core.Tests.Support;
using Strata.Core.Voxels;

namespace Strata.Core.Tests.Meshing;

public class MeshingTests
{
    private static readonly RenderTables Tables = new(TestPaths.Content, _ => 0);

    private static ChunkMeshResult Mesh(VoxelWorld world, Int3 chunk, Func<int, int, int>? skyTop = null, IReadOnlyList<LightSource>? sources = null)
    {
        var nb = Neighborhood.Capture(world, chunk) ?? throw new InvalidOperationException("missing neighbours");
        return ChunkMesher.Build(nb, Tables, skyTop ?? ((_, _) => 10_000), sources ?? Array.Empty<LightSource>());
    }

    private static VoxelWorld AirWorldWith(Action<EditBatch, ushort, ushort> edit)
    {
        var world = TestWorlds.Filled("air");
        var db = world.Content;
        var batch = world.BeginEdit();
        edit(batch, db.BlockId("stone"), db.BlockId("gray_brick"));
        batch.Commit();
        return world;
    }

    private static Dictionary<(long, long), int> EdgeBalance(MeshSurface s)
    {
        long Key(Vector3 v) => ((long)MathF.Round(v.X * 1000) * 73856093) ^ ((long)MathF.Round(v.Y * 1000) * 19349663) ^ ((long)MathF.Round(v.Z * 1000) * 83492791);
        var edges = new Dictionary<(long, long), int>();
        var idx = s.Indices;
        for (int t = 0; t < idx.Count; t += 3)
            for (int e = 0; e < 3; e++)
            {
                long a = Key(s.Positions[idx[t + e]]), b = Key(s.Positions[idx[t + (e + 1) % 3]]);
                if (a == b) continue;
                var key = a < b ? (a, b) : (b, a);
                edges[key] = edges.GetValueOrDefault(key) + (a < b ? 1 : -1);
            }
        return edges;
    }

    [Fact]
    public void ClosedObjectsAreWatertightAndConsistentlyWound()
    {
        var world = AirWorldWith((b, stone, brick) =>
        {
            for (int x = 40; x < 50; x++)
                for (int y = 40; y < 48; y++)
                    for (int z = 40; z < 49; z++)
                        if ((x - 45) * (x - 45) + (y - 44) * (y - 44) + (z - 44) * (z - 44) < 18) b.SetBlock(x, y, z, stone);
            b.SetBlock(50, 44, 44, brick);
            b.SetBlock(51, 44, 44, brick);
            b.SetBlock(51, 45, 44, brick);
        });
        var mesh = Mesh(world, new Int3(1, 1, 1));
        Assert.False(mesh.Opaque.IsEmpty);
        // Every edge must be traversed once in each direction (closed, consistently oriented surface).
        var unbalanced = EdgeBalance(mesh.Opaque).Where(kv => kv.Value != 0).ToList();
        Assert.True(unbalanced.Count == 0, $"{unbalanced.Count} unbalanced edges");
    }

    private static Vector3 Extent(MeshSurface s)
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var p in s.Positions)
        {
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
        }
        return max - min;
    }

    [Fact]
    public void SingleNaturalBlocksNeverVanish()
    {
        var world = AirWorldWith((b, stone, _) => b.SetBlock(48, 48, 48, stone));
        var mesh = Mesh(world, new Int3(1, 1, 1));
        Assert.True(mesh.Opaque.TriangleCount >= 12);
        var extent = Extent(mesh.Opaque).X;
        // An isolated natural voxel renders as a rounded nub (~1/3 cell); player-placed blocks are crisp cubes.
        Assert.True(extent >= 0.1f, $"single natural block collapsed to {extent} m");
    }

    [Fact]
    public void PlayerPlacedNaturalBlocksAreCrispCubes()
    {
        var world = AirWorldWith((b, stone, _) => b.SetBlock(48, 48, 48, stone, 0, CellFlags.PlayerPlaced));
        var mesh = Mesh(world, new Int3(1, 1, 1));
        Assert.Equal(12, mesh.Opaque.TriangleCount);
        Assert.Equal(0.5f, Extent(mesh.Opaque).X, 4);
    }

    [Fact]
    public void CrispBlocksProduceExactCubeFaces()
    {
        var world = AirWorldWith((b, _, brick) => b.SetBlock(40, 40, 40, brick));
        var mesh = Mesh(world, new Int3(1, 1, 1));
        Assert.Equal(12, mesh.Opaque.TriangleCount);
        foreach (var p in mesh.Opaque.Positions)
        {
            Assert.Equal(p.X * 2, MathF.Round(p.X * 2), 4);
            Assert.Equal(p.Y * 2, MathF.Round(p.Y * 2), 4);
        }
    }

    [Fact]
    public void FrontFacesAreClockwiseAgainstNormals()
    {
        var world = AirWorldWith((b, stone, _) =>
        {
            for (int x = 32; x < 64; x++)
                for (int z = 32; z < 64; z++)
                    for (int y = 32; y < 40; y++)
                        b.SetBlock(x, y, z, stone);
        });
        var s = Mesh(world, new Int3(1, 1, 1)).Opaque;
        for (int t = 0; t < s.Indices.Count; t += 3)
        {
            var a = s.Positions[s.Indices[t]];
            var n = Vector3.Cross(s.Positions[s.Indices[t + 1]] - a, s.Positions[s.Indices[t + 2]] - a);
            Assert.True(Vector3.Dot(n, s.Normals[s.Indices[t]]) <= 1e-4f, "triangle not clockwise from its front");
        }
    }

    [Fact]
    public void GeneratedTerrainBordersMatchBetweenChunks()
    {
        var gen = new Strata.Core.WorldGen.WorldGenerator(TestPaths.Content, new World.WorldSettings { Seed = 1234, Size = World.WorldSizePreset.Tiny });
        var world = new VoxelWorld(TestPaths.Content, gen.Plan.Size, TestPaths.Content.BlockId("abyssal_rock"));
        var a = ChunkMath.CellToChunk(gen.Plan.Spawn);
        for (int dx = -1; dx <= 2; dx++)
            for (int dy = -1; dy <= 1; dy++)
                for (int dz = -1; dz <= 1; dz++)
                {
                    var c = a + new Int3(dx, dy, dz);
                    world.AddChunk(new Chunk(c, gen.Generate(c).Data));
                }
        var ma = Mesh(world, a);
        var mb = Mesh(world, a + new Int3(1, 0, 0));
        float edge = ChunkMath.Size * Units.CellSize;
        var onA = ma.Opaque.Positions.Where(p => MathF.Abs(p.X - edge) < 1e-4f).Select(p => (MathF.Round(p.Y, 3), MathF.Round(p.Z, 3))).ToHashSet();
        var onB = mb.Opaque.Positions.Where(p => MathF.Abs(p.X) < 1e-4f).Select(p => (MathF.Round(p.Y, 3), MathF.Round(p.Z, 3))).ToHashSet();
        Assert.NotEmpty(onA);
        Assert.True(onA.SetEquals(onB), $"border vertices differ: {onA.Except(onB).Count()} only in A, {onB.Except(onA).Count()} only in B");
    }

    [Fact]
    public void MeshingIsDeterministic()
    {
        var world = AirWorldWith((b, stone, brick) =>
        {
            for (int i = 0; i < 200; i++) b.SetBlock(32 + (i * 7) % 32, 32 + (i * 13) % 32, 32 + (i * 3) % 32, i % 3 == 0 ? brick : stone);
        });
        var m1 = Mesh(world, new Int3(1, 1, 1));
        var m2 = Mesh(world, new Int3(1, 1, 1));
        Assert.Equal(m1.Opaque.Positions, m2.Opaque.Positions);
        Assert.Equal(m1.Opaque.Indices, m2.Opaque.Indices);
    }

    [Fact]
    public void TorchLightFallsOffAndSkyStaysOutOfSealedCaves()
    {
        var world = TestWorlds.Filled("stone");
        var batch = world.BeginEdit();
        for (int x = 40; x < 60; x++) batch.SetBlock(x, 48, 48, 0);
        batch.Commit();
        var torch = new LightSource(new Int3(40, 48, 48), LightValue.Pack(0, 14, 11, 7));
        var mesh = Mesh(world, new Int3(1, 1, 1), skyTop: (_, _) => 10_000, sources: new[] { torch });
        ushort At(int x) => mesh.Light[ChunkMath.Index(x - 32, 16, 16)];
        Assert.Equal(14, LightValue.R(At(40)));
        Assert.Equal(9, LightValue.R(At(45)));
        Assert.Equal(0, LightValue.R(At(55)));
        Assert.Equal(0, LightValue.Sky(At(45)));
    }

    [Fact]
    public void SkyLightFloodsOpenSurfaces()
    {
        var world = AirWorldWith((b, stone, _) =>
        {
            for (int x = 32; x < 64; x++)
                for (int z = 32; z < 64; z++)
                    for (int y = 32; y < 40; y++)
                        b.SetBlock(x, y, z, stone);
        });
        var mesh = Mesh(world, new Int3(1, 1, 1), skyTop: (_, _) => 40);
        Assert.Equal(15, LightValue.Sky(mesh.Light[ChunkMath.Index(10, 10, 10)]));
        Assert.Equal(0, LightValue.Sky(mesh.Light[ChunkMath.Index(10, 3, 10)]));
    }

    /// <summary>A 3×3×3-chunk air world with stone ground at y 32–39 and a stone roof slab over the middle.</summary>
    private static (VoxelWorld World, SkyMap Sky) GroundUnderRoof(int roofBottom, int roofTop)
    {
        var world = AirWorldWith((b, stone, _) =>
        {
            for (int x = 0; x < 96; x++)
                for (int z = 0; z < 96; z++)
                {
                    for (int y = 32; y < 40; y++) b.SetBlock(x, y, z, stone);
                    if (x is >= 16 and < 80 && z is >= 16 and < 80)
                        for (int y = roofBottom; y <= roofTop; y++) b.SetBlock(x, y, z, stone);
                }
        });
        var sky = new SkyMap(world, Tables, (_, _) => 40, (_, _) => 2);
        for (int cz = 0; cz < 3; cz++)
            for (int cx = 0; cx < 3; cx++) sky.Rebuild(cx, cz);
        return (world, sky);
    }

    [Fact]
    public void GroundUnderAFloatingRoofIsOutdoorShadeNotACave()
    {
        var (world, sky) = GroundUnderRoof(roofBottom: 70, roofTop: 74);
        Assert.Equal(75, sky.SkyTop(48, 48));
        Assert.Equal(40, sky.ShadeTop(48, 48));
        Assert.Equal(40, sky.ShadeTop(4, 4)); // open sky: shade equals the sky top
        Assert.Equal(40, sky.SkyTop(4, 4));
        var nb = Neighborhood.Capture(world, new Int3(1, 1, 1))!;
        var shaded = ChunkMesher.Build(nb, Tables, sky.SkyTop, Array.Empty<LightSource>(), sky.ShadeTop);
        var dark = ChunkMesher.Build(nb, Tables, sky.SkyTop, Array.Empty<LightSource>());
        int centre = ChunkMath.Index(16, 10, 16); // (48, 42, 48): on the ground, 30 cells under the roof's middle
        Assert.Equal(LightEngine.ShadeLight, LightValue.Sky(shaded.Light[centre]));
        Assert.Equal(0, LightValue.Sky(dark.Light[centre]));
    }

    [Fact]
    public void LowRoofsAndTheTerrainItselfStayShadowed()
    {
        var (world, sky) = GroundUnderRoof(roofBottom: 46, roofTop: 48); // a 6-cell room, like a house
        Assert.Equal(sky.SkyTop(48, 48), sky.ShadeTop(48, 48));
        var nb = Neighborhood.Capture(world, new Int3(1, 1, 1))!;
        var mesh = ChunkMesher.Build(nb, Tables, sky.SkyTop, Array.Empty<LightSource>(), sky.ShadeTop);
        Assert.Equal(0, LightValue.Sky(mesh.Light[ChunkMath.Index(16, 10, 16)]));
        // Solid ground under open sky has no shade range either.
        Assert.Equal(sky.SkyTop(4, 4), sky.ShadeTop(4, 4));
    }
}
