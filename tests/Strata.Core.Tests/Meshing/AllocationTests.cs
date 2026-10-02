using Strata.Core.Common;
using Strata.Core.Content.Loading;
using Strata.Core.Lighting;
using Strata.Core.Meshing;
using Strata.Core.Presentation;
using Strata.Core.Tests.Support;
using Strata.Core.World;
using Strata.Core.WorldGen;
using Strata.Core.WorldGen.Passes;

namespace Strata.Core.Tests.Meshing;

/// <summary>
/// Large per-job buffers are reused per thread instead of allocated on the large-object heap every time (gen2
/// collections are what freeze the game while exploring).
/// </summary>
public class AllocationTests
{
    private static readonly FileSystemDataSource Data = new(TestPaths.ProjectRoot);
    private static readonly RenderTables Tables = new(TestPaths.Content, TextureLayerCatalog.Load(Data).IndexOf, GrassConfig.Load(Data));

    [Fact]
    public void MeshingReusesVolumeBuffers()
    {
        // A small stone block in an air chunk: the mesh itself is tiny, so the volume dominates any per-job garbage.
        var world = TestWorlds.Filled("air");
        var batch = world.BeginEdit();
        ushort stone = world.Content.BlockId("stone");
        for (int x = 44; x < 50; x++)
            for (int y = 44; y < 50; y++)
                for (int z = 44; z < 50; z++)
                    batch.SetBlock(x, y, z, stone);
        batch.Commit();
        var chunk = new Int3(1, 1, 1);
        Build(world, chunk); // warms the per-thread pools
        long before = GC.GetAllocatedBytesForCurrentThread();
        Build(world, chunk);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated < 200_000, $"second mesh build allocated {allocated} bytes");
    }

    private static void Build(Strata.Core.Voxels.VoxelWorld world, Int3 chunk)
    {
        var nb = Neighborhood.Capture(world, chunk) ?? throw new InvalidOperationException("missing neighbours");
        var result = ChunkMesher.Build(nb, Tables, (_, _) => 0, Array.Empty<LightSource>());
        Assert.False(result.Opaque.IsEmpty);
    }

    [Fact]
    public void CaveGenerationReusesScratch()
    {
        var gen = new WorldGenerator(TestPaths.Content, new WorldSettings { Seed = 1234, Size = WorldSizePreset.Tiny });
        var caves = gen.Passes.OfType<CaveGenerationPass>().Single();
        // A deep, solid chunk: noise caves sample all three dense fields there (cheese only exists deep down).
        var ctx = gen.GenerateContext(new Int3(3, 1, 3));
        Assert.False(ctx.AllAir);
        Assert.True(ctx.Depth(ctx.Origin.Y) > 0.12f, "the chunk must be deep enough for cheese caverns");
        caves.Generate(ctx);
        long before = GC.GetAllocatedBytesForCurrentThread();
        caves.Generate(ctx);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated < 64_000, $"second cave pass allocated {allocated} bytes");
    }
}
