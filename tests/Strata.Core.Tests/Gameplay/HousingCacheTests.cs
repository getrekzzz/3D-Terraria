using Strata.Core.Common;
using Strata.Core.Gameplay.Housing;
using Strata.Core.Tests.Support;
using Strata.Core.Voxels;

namespace Strata.Core.Tests.Gameplay;

/// <summary>P5-02: evil blocks corrupt houses, Terraria's messages, and the cached room scans.</summary>
public class HousingCacheTests
{
    private static Int3 Inside(TestGame g) => g.Floor + new Int3(-2, 2, -2);

    /// <summary>Two layers of a block under the whole house (its floor and the layer below).</summary>
    private static void Underlay(TestGame g, string block)
    {
        var id = g.Content.BlockId(block);
        var batch = g.World.Session.World.BeginEdit();
        for (int y = -1; y <= 0; y++)
            for (int z = -5; z <= 5; z++)
                for (int x = -5; x <= 5; x++)
                    batch.SetBlock(g.Floor.X + x, g.Floor.Y + y, g.Floor.Z + z, id);
        g.World.Session.Commit(batch);
    }

    [Fact]
    public void EvilBlocksCorruptAHouse()
    {
        using var g = TestGame.Create();
        TestHouses.Build(g, g.Floor);
        Assert.Equal("This housing is suitable.", g.World.Rooms.Get(Inside(g))!.Message);
        Underlay(g, "ebonstone");
        var room = g.World.Rooms.Get(Inside(g))!;
        Assert.False(room.Valid);
        Assert.Equal("This housing is corrupted.", room.Message);
    }

    [Fact]
    public void OreDoesNotCorrupt()
    {
        using var g = TestGame.Create();
        TestHouses.Build(g, g.Floor);
        Underlay(g, "demonite_ore");
        Assert.True(new HousingScanner(g.World.Session.World).Scan(Inside(g)).Valid);
    }

    [Fact]
    public void ScansAreCachedUntilANearbyBlockChanges()
    {
        using var g = TestGame.Create();
        TestHouses.Build(g, g.Floor);
        var rooms = g.World.Rooms;
        var first = rooms.Get(Inside(g))!;
        long scans = rooms.Scans;
        Assert.Same(first, rooms.Get(Inside(g) + new Int3(1, 0, 1))); // any interior cell finds it
        Assert.Equal(scans, rooms.Scans);

        // Water poured inside changes no block: the cached room stays.
        var batch = g.World.Session.World.BeginEdit();
        batch.SetLiquid(Inside(g).X, Inside(g).Y, Inside(g).Z, LiquidCode.Pack(LiquidType.Water, 4));
        g.World.Session.Commit(batch);
        Assert.Same(first, rooms.Get(Inside(g)));

        // Breaking a wall block drops it: the next query scans again and finds the room open.
        g.World.Editor.SetBlock(g.Floor + new Int3(5, 3, 0), 0);
        var after = rooms.Get(Inside(g))!;
        Assert.Equal(scans + 1, rooms.Scans);
        Assert.False(after.Valid);
    }

    [Fact]
    public void BudgetedCallersWaitForTheNextTick()
    {
        using var g = TestGame.Create();
        g.World.Housing.Config = new HousingConfig { MaxScansPerTick = 1 };
        TestHouses.Build(g, g.Floor + new Int3(-7, 0, 0));
        TestHouses.Build(g, g.Floor + new Int3(7, 0, 0));
        var rooms = g.World.Rooms;
        Assert.NotNull(rooms.Get(g.Floor + new Int3(-9, 2, -2), budgeted: true));
        Assert.Null(rooms.Get(g.Floor + new Int3(5, 2, -2), budgeted: true));
        Assert.NotNull(rooms.Get(g.Floor + new Int3(5, 2, -2))); // explicit queries are never refused
        g.World.Session.Clock.Advance();
        Assert.NotNull(rooms.Get(g.Floor + new Int3(-9, 2, -2), budgeted: true)); // cached: no scan needed
    }
}
