using Strata.Core.Common;
using Strata.Core.Lighting;
using Strata.Core.Meshing;
using Strata.Core.Tests.Support;
using Strata.Core.Voxels;

namespace Strata.Core.Tests.Meshing;

/// <summary>Per-bucket collision hashes: colliders rebuild only where the triangles actually changed.</summary>
public class CollisionHashTests
{
    private static readonly RenderTables Tables = new(TestPaths.Content, _ => 0);
    private static readonly Int3 Chunk = new(1, 1, 1);

    private static ChunkMeshResult Mesh(VoxelWorld world, params LightSource[] lights)
    {
        var nb = Neighborhood.Capture(world, Chunk) ?? throw new InvalidOperationException("missing neighbours");
        return ChunkMesher.Build(nb, Tables, (_, _) => 10_000, lights);
    }

    /// <summary>Rolling stone terrain whose surface crosses the centre chunk's lower buckets.</summary>
    private static VoxelWorld Terrain()
    {
        var world = TestWorlds.Filled("air", 3);
        ushort stone = world.Content.BlockId("stone");
        var batch = world.BeginEdit();
        int size = 3 * ChunkMath.Size;
        for (int x = 0; x < size; x++)
            for (int z = 0; z < size; z++)
                for (int y = 0; y < 40 + (x / 5 + z / 7) % 3; y++) batch.SetBlock(x, y, z, stone);
        batch.Commit();
        return world;
    }

    private static void Edit(VoxelWorld world, Action<EditBatch> edit)
    {
        var batch = world.BeginEdit();
        edit(batch);
        batch.Commit();
    }

    private static List<int> Changed(ulong[] a, ulong[] b)
    {
        var changed = new List<int>();
        for (int i = 0; i < a.Length; i++)
            if (a[i] != b[i]) changed.Add(i);
        return changed;
    }

    [Fact]
    public void Deterministic()
    {
        var world = Terrain();
        var a = Mesh(world).Collision.Hashes!;
        var b = Mesh(world).Collision.Hashes!;
        Assert.Equal(CollisionBuilder.HashCount, a.Length);
        Assert.Equal(a, b);
        Assert.Contains(a, h => h != 0);
        Assert.Contains(a, h => h == 0); // the upper buckets are empty air
    }

    [Fact]
    public void LiquidOnlyChangeKeepsHashes()
    {
        var world = Terrain();
        var before = Mesh(world);
        Edit(world, b => b.SetLiquid(48, 43, 48, LiquidCode.Full(LiquidType.Water)));
        var after = Mesh(world);
        Assert.True(before.Liquid.IsEmpty && !after.Liquid.IsEmpty, "the liquid surface should differ");
        Assert.Equal(before.Collision.Hashes, after.Collision.Hashes);
    }

    [Fact]
    public void LightSourceKeepsHashes()
    {
        var world = Terrain();
        var dark = Mesh(world).Collision.Hashes!;
        var lit = Mesh(world, new LightSource(new Int3(48, 45, 48), 0xFFF)).Collision.Hashes!;
        Assert.Equal(dark, lit);
    }

    [Fact]
    public void MiningChangesOnlyNearbyBuckets()
    {
        var world = Terrain();
        var before = Mesh(world).Collision.Hashes!;
        var cell = new Int3(40, 39, 40); // local (8, 7, 8): well inside solid bucket 0
        Assert.NotEqual(0, world.GetBlock(cell));
        Edit(world, b => b.SetBlock(cell, 0));
        var after = Mesh(world).Collision.Hashes!;
        var changed = Changed(before, after);
        Assert.NotEmpty(changed);
        int bucket = CollisionBuilder.BucketOf(new System.Numerics.Vector3(8.5f, 7.5f, 8.5f) * Units.CellSize);
        var allowed = new[] { bucket, bucket ^ 1, bucket ^ 2, bucket ^ 4 };
        Assert.All(changed, i => Assert.Contains(i, allowed));
    }
}
