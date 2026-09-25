using Strata.Core.Common;
using Strata.Core.Tests.Support;
using Strata.Core.Voxels.TileObjects;

namespace Strata.Core.Tests.Voxels;

public class TileObjectStoreTests
{
    [Fact]
    public void RotationSwapsFootprint()
    {
        var def = TestPaths.Content.TileObjects.Get("wooden_table"); // 4 x 2 x 3
        Assert.Equal(new Int3(4, 2, 3), TileObjectInstance.FootprintOf(def, 0));
        Assert.Equal(new Int3(3, 2, 4), TileObjectInstance.FootprintOf(def, 1));
    }

    [Fact]
    public void OverlapsAreRejectedAndOccupancyTracked()
    {
        var store = new TileObjectStore();
        var table = TestPaths.Content.TileObjects.Get("wooden_table");
        var a = new TileObjectInstance(table, new Int3(10, 10, 10), 0, 0);
        Assert.True(store.TryAdd(a));
        Assert.Same(a, store.At(new Int3(13, 11, 12)));
        Assert.Null(store.At(new Int3(14, 10, 10)));
        Assert.False(store.TryAdd(new TileObjectInstance(table, new Int3(12, 10, 10), 0, 0)));
        Assert.True(store.Remove(a));
        Assert.False(store.IsOccupied(new Int3(10, 10, 10)));
    }

    [Fact]
    public void DetachChunkRemovesAnchoredObjects()
    {
        var store = new TileObjectStore();
        var torch = TestPaths.Content.TileObjects.Get("torch");
        store.TryAdd(new TileObjectInstance(torch, new Int3(1, 1, 1), 0, 0));
        store.TryAdd(new TileObjectInstance(torch, new Int3(40, 1, 1), 0, 0));
        var detached = store.DetachChunk(Int3.Zero);
        Assert.Single(detached);
        Assert.Equal(1, store.Count);
        Assert.False(store.IsOccupied(new Int3(1, 1, 1)));
    }

    [Fact]
    public void ContainersAreSizedFromDefinition()
    {
        var chest = TestPaths.Content.TileObjects.Get("wooden_chest");
        var obj = new TileObjectInstance(chest, Int3.Zero, 0, 0);
        Assert.Equal(40, obj.Container!.Length);
    }
}
