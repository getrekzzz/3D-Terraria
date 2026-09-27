using Strata.Core.Common;
using Strata.Core.Tests.Support;
using Strata.Core.Voxels;

namespace Strata.Core.Tests.Voxels;

public class EditBatchTests
{
    [Fact]
    public void EditsAreCopyOnWrite()
    {
        var world = TestWorlds.Filled();
        var chunk = world.GetChunk(new Int3(1, 1, 1))!;
        var before = chunk.Capture();
        int version = chunk.Version;

        var batch = world.BeginEdit();
        Assert.True(batch.SetBlock(40, 40, 40, 0));
        var result = batch.Commit();

        Assert.True(before.IsUniform); // old snapshot untouched
        Assert.Equal(0, world.GetBlock(40, 40, 40));
        Assert.Equal(version + 1, chunk.Version);
        Assert.True(chunk.Modified);
        Assert.Single(result.Changes);
        Assert.Contains(new Int3(1, 1, 1), result.ChangedChunks);
    }

    [Fact]
    public void EditNearBorderAffectsNeighbours()
    {
        var world = TestWorlds.Filled();
        var batch = world.BeginEdit();
        batch.SetBlock(33, 48, 48, 0); // just inside chunk x=1, near its -X face
        var result = batch.Commit();
        Assert.Contains(new Int3(0, 1, 1), result.AffectedChunks);
        Assert.Contains(new Int3(1, 1, 1), result.AffectedChunks);
    }

    [Fact]
    public void EditsToMissingChunksAreRejected()
    {
        var world = TestWorlds.Filled(chunks: 2);
        world.RemoveChunk(new Int3(1, 0, 0), out _);
        var batch = world.BeginEdit();
        Assert.False(batch.SetBlock(40, 5, 5, 0));
        Assert.False(batch.SetBlock(-1, 5, 5, 0));
        Assert.Equal(VoxelWorld.Unknown, world.GetBlock(40, 5, 5));
    }

    [Fact]
    public void RestoringUniformContentCompactsChunk()
    {
        var world = TestWorlds.Filled();
        var stone = world.Content.BlockId("stone");
        var b1 = world.BeginEdit();
        b1.SetBlock(5, 5, 5, 0);
        b1.Commit();
        Assert.False(world.GetChunk(Int3.Zero)!.IsUniform);
        var b2 = world.BeginEdit();
        b2.SetBlock(5, 5, 5, stone);
        b2.Commit();
        Assert.True(world.GetChunk(Int3.Zero)!.IsUniform);
    }

    [Fact]
    public void RaycastFindsFirstSolidCellAndFace()
    {
        var world = TestWorlds.Filled("air");
        var stone = world.Content.BlockId("stone");
        var batch = world.BeginEdit();
        batch.SetBlock(10, 5, 5, stone);
        batch.Commit();
        var hit = VoxelRaycast.Cast(world, new System.Numerics.Vector3(2.5f, 5.5f, 5.5f), System.Numerics.Vector3.UnitX, 20, b => b != 0);
        Assert.NotNull(hit);
        Assert.Equal(new Int3(10, 5, 5), hit!.Value.Cell);
        Assert.Equal(new Int3(-1, 0, 0), hit.Value.Normal);
        Assert.InRange(hit.Value.Distance, 7.4f, 7.6f);
    }
}
