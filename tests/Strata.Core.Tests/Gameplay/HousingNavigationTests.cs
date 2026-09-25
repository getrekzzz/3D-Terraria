using System.Numerics;
using Strata.Core.Common;
using Strata.Core.Gameplay.Housing;
using Strata.Core.Gameplay.Navigation;
using Strata.Core.Physics;
using Strata.Core.Tests.Support;
using Strata.Core.Voxels;
using Strata.Core.Voxels.TileObjects;

namespace Strata.Core.Tests.Gameplay;

public class HousingNavigationTests
{
    /// <summary>
    /// A 9×9 room, 6 cells tall, walled with player-built bricks on the arena floor, a door in the −Z wall, a torch,
    /// a table and a chair. Returns the door.
    /// </summary>
    private static TileObjectInstance BuildHouse(TestGame g, bool built = true, bool chair = true)
    {
        var f = g.Floor;
        var world = g.World.Session.World;
        var brick = g.Content.BlockId("stone_brick");
        var flags = built ? CellFlags.PlayerPlaced : CellFlags.None;
        var batch = world.BeginEdit();
        for (int y = 1; y <= 7; y++)
            for (int z = -5; z <= 5; z++)
                for (int x = -5; x <= 5; x++)
                {
                    bool shell = y == 7 || Math.Abs(x) == 5 || Math.Abs(z) == 5;
                    batch.SetBlock(f.X + x, f.Y + y, f.Z + z, shell ? brick : (ushort)0, 0, shell ? flags : CellFlags.None);
                }
        // Door opening in the −Z wall.
        for (int y = 1; y <= 4; y++)
            for (int x = 0; x <= 1; x++)
                batch.SetBlock(f.X + x, f.Y + y, f.Z - 5, 0);
        g.World.Session.Commit(batch);
        var store = world.TileObjects;
        var door = new TileObjectInstance(g.Content.TileObjects.Get("wooden_door"), f + new Int3(0, 1, -5), 0, 0);
        Assert.True(store.TryAdd(door));
        Assert.True(store.TryAdd(new TileObjectInstance(g.Content.TileObjects.Get("torch"), f + new Int3(-4, 1, 4), 0, 0)));
        Assert.True(store.TryAdd(new TileObjectInstance(g.Content.TileObjects.Get("wooden_table"), f + new Int3(1, 1, 1), 0, 0)));
        if (chair) Assert.True(store.TryAdd(new TileObjectInstance(g.Content.TileObjects.Get("wooden_chair"), f + new Int3(-3, 1, 1), 0, 0)));
        return door;
    }

    private static Int3 Inside(TestGame g) => g.Floor + new Int3(-2, 2, -2);

    [Fact]
    public void BuiltFurnishedRoomIsAHome()
    {
        using var g = TestGame.Create();
        BuildHouse(g);
        var room = new HousingScanner(g.World.Session.World).Scan(Inside(g));
        Assert.True(room.Valid, room.ToString());
        Assert.NotNull(room.HomeNode);
        Assert.Contains(room.Furniture, o => o.Def.Id == "wooden_door");
        Assert.Equal(81, room.FloorCells); // 9×9 floor; furniture stands on it
    }

    [Fact]
    public void ShippedHousingRulesLoadAndMatchTheDefaults()
    {
        var shipped = HousingConfig.Load(new Strata.Core.Content.Loading.FileSystemDataSource(TestPaths.ProjectRoot));
        var defaults = new HousingConfig();
        Assert.Equal(defaults.MinFloorCells, shipped.MinFloorCells);
        Assert.Equal(defaults.MaxVolume, shipped.MaxVolume);
        Assert.Equal(defaults.Requirements.Select(r => (r.Role, r.Where)), shipped.Requirements.Select(r => (r.Role, r.Where)));
        foreach (var r in shipped.Requirements)
            Assert.Contains(TestPaths.Content.TileObjects.All, o => o.Housing.Contains(r.Role));
    }

    [Fact]
    public void RoomProblemsAreReported()
    {
        using var g = TestGame.Create();
        BuildHouse(g, chair: false);
        var scanner = new HousingScanner(g.World.Session.World);
        Assert.Contains("needs a chair", scanner.Scan(Inside(g)).Problems);

        // A hole in the wall opens the room to the world.
        g.World.Editor.SetBlock(g.Floor + new Int3(5, 3, 0), 0);
        Assert.Contains("not enclosed or too large", scanner.Scan(Inside(g)).Problems);
    }

    [Fact]
    public void NaturalCavesAreNotHomes()
    {
        using var g = TestGame.Create();
        BuildHouse(g, built: false);
        var room = new HousingScanner(g.World.Session.World).Scan(Inside(g));
        Assert.Contains("walls must be built, not natural", room.Problems);
    }

    [Fact]
    public void PathLeadsThroughTheDoorIntoTheHouse()
    {
        using var g = TestGame.Create();
        BuildHouse(g);
        var world = g.World.Session.World;
        var nav = new VoxelNavigator(world);
        var home = new HousingScanner(world).Scan(Inside(g)).HomeNode!.Value;
        var outside = g.Floor + new Int3(1, 1, -9);
        Assert.True(nav.IsStandable(outside, NavAgent.Humanoid));

        var path = nav.FindPath(outside, home, NavAgent.Humanoid);
        Assert.NotNull(path);
        Assert.Equal(home, path![^1]);
        // It passes through the doorway, where the walker has to open the door.
        Assert.Contains(path, n => nav.DoorsAt(n, NavAgent.Humanoid).Any());

        // A walker that cannot open doors is shut out.
        Assert.Null(nav.FindPath(outside, home, NavAgent.Humanoid with { OpensDoors = false }));
    }

    [Fact]
    public void PathsClimbLedgesWithinJumpHeight()
    {
        using var g = TestGame.Create();
        var world = g.World.Session.World;
        var nav = new VoxelNavigator(world);
        var stone = g.Content.BlockId("stone");
        var f = g.Floor;
        // Two raised platforms: 2 cells high (jumpable) and 3 cells high (not), 4×4 each, away from the centre.
        var batch = world.BeginEdit();
        for (int z = 0; z < 4; z++)
            for (int x = 0; x < 4; x++)
            {
                for (int y = 1; y <= 2; y++) batch.SetBlock(f.X + 5 + x, f.Y + y, f.Z + z, stone);
                for (int y = 1; y <= 3; y++) batch.SetBlock(f.X - 8 + x, f.Y + y, f.Z + z, stone);
            }
        g.World.Session.Commit(batch);
        var start = f + new Int3(0, 1, 2);
        var low = f + new Int3(7, 3, 2);
        var high = f + new Int3(-6, 4, 2);
        Assert.True(nav.IsStandable(low, NavAgent.Humanoid));
        Assert.True(nav.IsStandable(high, NavAgent.Humanoid));

        var up = nav.FindPath(start, low, NavAgent.Humanoid);
        Assert.NotNull(up);
        Assert.Null(nav.FindPath(start, high, NavAgent.Humanoid, maxNodes: 3000));
        // Coming back down is a drop.
        Assert.NotNull(nav.FindPath(low, start, NavAgent.Humanoid));
    }

    [Fact]
    public void ClosedDoorsStopBodiesOpenDoorsDoNot()
    {
        using var g = TestGame.Create();
        var door = BuildHouse(g);
        var world = g.World.Session.World;
        var inDoor = (new Vector3(door.Anchor.X + 1, door.Anchor.Y + 1, door.Anchor.Z) + new Vector3(0, 0.5f, 0.5f)) * Units.CellSize;
        var half = new Vector3(0.3f, 0.3f, 0.2f);
        Assert.True(VoxelMotion.Overlaps(world, inDoor, half));
        door.Active = true;
        Assert.False(VoxelMotion.Overlaps(world, inDoor, half));
    }
}
