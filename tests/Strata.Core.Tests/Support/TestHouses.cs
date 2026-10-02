using Strata.Core.Common;
using Strata.Core.Voxels;
using Strata.Core.Voxels.TileObjects;

namespace Strata.Core.Tests.Support;

/// <summary>Builds furnished test houses on the arena floor.</summary>
public static class TestHouses
{
    /// <summary>
    /// A 9×9 room, 6 cells tall, walled with (player-built) bricks around <paramref name="floor"/> (a floor cell), a
    /// door in the −Z wall at x 0..1, a torch, a table and a chair. Returns the door. <paramref name="set"/> swaps the
    /// furniture (door, light, table, chair ids; default the wooden set and a torch).
    /// </summary>
    public static TileObjectInstance Build(TestGame g, Int3 floor, bool built = true, bool chair = true, string[]? set = null)
    {
        set ??= new[] { "wooden_door", "torch", "wooden_table", "wooden_chair" };
        var world = g.World.Session.World;
        var brick = g.Content.BlockId("gray_brick");
        var flags = built ? CellFlags.PlayerPlaced : CellFlags.None;
        var batch = world.BeginEdit();
        for (int y = 1; y <= 7; y++)
            for (int z = -5; z <= 5; z++)
                for (int x = -5; x <= 5; x++)
                {
                    bool shell = y == 7 || Math.Abs(x) == 5 || Math.Abs(z) == 5;
                    batch.SetBlock(floor.X + x, floor.Y + y, floor.Z + z, shell ? brick : (ushort)0, 0, shell ? flags : CellFlags.None);
                }
        for (int y = 1; y <= 4; y++)
            for (int x = 0; x <= 1; x++)
                batch.SetBlock(floor.X + x, floor.Y + y, floor.Z - 5, 0);
        g.World.Session.Commit(batch);
        var store = world.TileObjects;
        var door = new TileObjectInstance(g.Content.TileObjects.Get(set[0]), floor + new Int3(0, 1, -5), 0, 0);
        Assert.True(store.TryAdd(door));
        Assert.True(store.TryAdd(new TileObjectInstance(g.Content.TileObjects.Get(set[1]), floor + new Int3(-4, 1, 4), 0, 0)));
        Assert.True(store.TryAdd(new TileObjectInstance(g.Content.TileObjects.Get(set[2]), floor + new Int3(1, 1, 1), 0, 0)));
        if (chair) Assert.True(store.TryAdd(new TileObjectInstance(g.Content.TileObjects.Get(set[3]), floor + new Int3(-3, 1, 1), 0, 0)));
        return door;
    }
}
