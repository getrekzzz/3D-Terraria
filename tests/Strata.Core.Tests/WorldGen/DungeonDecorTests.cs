using Strata.Core.Common;
using Strata.Core.Content.Defs;
using Strata.Core.Gameplay.Building;
using Strata.Core.Tests.Support;
using Strata.Core.WorldGen.Structures;
using Strata.Core.WorldGen.Structures.Dungeon;

namespace Strata.Core.Tests.WorldGen;

/// <summary>The furnished Dungeon of seed 1234 (Tiny): placements, room roles, budgets and the wired dart traps,
/// checked in the blueprint and in the installed world.</summary>
public class DungeonDecorTests
{
    private static List<ObjectOp> Objects(DungeonFeature f) => f.Blueprint.Ops.OfType<ObjectOp>().ToList();

    [Fact]
    public void NoObjectsOverlap()
    {
        var g = DungeonGenerationTests.Loaded();
        var ops = Objects(DungeonGenerationTests.Feature(g));
        var cells = new Dictionary<Int3, ObjectOp>();
        foreach (var op in ops)
            for (int y = op.Bounds.Min.Y; y < op.Bounds.Max.Y; y++)
                for (int z = op.Bounds.Min.Z; z < op.Bounds.Max.Z; z++)
                    for (int x = op.Bounds.Min.X; x < op.Bounds.Max.X; x++)
                        Assert.True(cells.TryAdd(new Int3(x, y, z), op), $"{op.ObjectId}@{op.Anchor} overlaps another object at ({x},{y},{z})");
        // Every planned object made it into the world (the store rejects overlaps silently).
        var world = g.World.Session.World;
        foreach (var op in ops) Assert.True(world.TileObjects.At(op.Anchor) is { } inst && inst.Def.Id == op.ObjectId, $"{op.ObjectId}@{op.Anchor} is missing");
        Assert.Contains("book", DungeonGenerationTests.Feature(g).ObjectIds);
    }

    [Fact]
    public void WallObjectsHangOnWallsAndCeilingObjectsTouchCeilings()
    {
        var g = DungeonGenerationTests.Loaded();
        var world = g.World.Session.World;
        int walls = 0, ceilings = 0;
        foreach (var op in Objects(DungeonGenerationTests.Feature(g)))
        {
            var obj = world.TileObjects.At(op.Anchor)!;
            var b = obj.Bounds;
            for (int y = b.Min.Y; y < b.Max.Y; y++)
                for (int z = b.Min.Z; z < b.Max.Z; z++)
                    for (int x = b.Min.X; x < b.Max.X; x++)
                        Assert.False(world.IsSolid(x, y, z), $"{obj} is buried at ({x},{y},{z})");
            Assert.True(PlacementRules.IsSupported(world, obj), $"{obj} is not held up");
            if (obj.Def.Anchor == TileAnchor.Wall)
            {
                walls++;
                bool side = false;
                for (int z = b.Min.Z; z < b.Max.Z; z++) side |= world.IsSolid(b.Min.X - 1, b.Min.Y, z) || world.IsSolid(b.Max.X, b.Min.Y, z);
                for (int x = b.Min.X; x < b.Max.X; x++) side |= world.IsSolid(x, b.Min.Y, b.Min.Z - 1) || world.IsSolid(x, b.Min.Y, b.Max.Z);
                Assert.True(side, $"{obj} does not hang on a wall");
            }
            else if (obj.Def.Anchor == TileAnchor.Ceiling)
            {
                ceilings++;
                for (int z = b.Min.Z; z < b.Max.Z; z++)
                    for (int x = b.Min.X; x < b.Max.X; x++) Assert.True(world.IsSolid(x, b.Max.Y, z), $"{obj} does not touch the ceiling");
            }
            else if (obj.Def.Anchor == TileAnchor.Floor && obj.Def.Id != "candle")
                for (int z = b.Min.Z; z < b.Max.Z; z++)
                    for (int x = b.Min.X; x < b.Max.X; x++) Assert.True(world.IsSolid(x, b.Min.Y - 1, z), $"{obj} overhangs the floor");
        }
        Assert.True(walls >= 4 && ceilings >= 4, $"{walls} wall and {ceilings} ceiling objects");
    }

    [Fact]
    public void LibrariesHoldBooksAndAtLeastOneWaterBolt()
    {
        var g = DungeonGenerationTests.Loaded();
        var f = DungeonGenerationTests.Feature(g);
        var ops = Objects(f);
        var libraries = f.Layout.Rooms.Where(r => r.Kind == RoomKind.Library).ToList();
        Assert.NotEmpty(libraries);
        foreach (var room in libraries)
        {
            Assert.True(ops.Count(o => o.ObjectId == "book" && room.Interior.Contains(o.Anchor)) >= 6, $"library {room.Id} has too few books");
            Assert.Contains(ops, o => o.ObjectId.EndsWith("_bookcase") && room.Interior.Contains(o.Anchor));
        }
        Assert.Contains(ops, o => o.ObjectId == "book" && o.Loot == DungeonDecorator.WaterBoltLoot);
    }

    [Fact]
    public void TreasureRoomsHaveAChest()
    {
        var g = DungeonGenerationTests.Loaded();
        var f = DungeonGenerationTests.Feature(g);
        var ops = Objects(f);
        foreach (var room in f.Layout.Rooms.Where(r => r.Kind == RoomKind.Treasure))
            Assert.Contains(ops, o => o.ObjectId == "gold_chest" && room.Interior.Contains(o.Anchor));
    }

    [Fact]
    public void TheMechanicsSpotStaysClear()
    {
        var g = DungeonGenerationTests.Loaded();
        var l = DungeonGenerationTests.Feature(g).Layout;
        var m = l.MechanicCell;
        Assert.True(l.MechanicRoom >= 0);
        Assert.Empty(g.World.Session.World.TileObjects.InBox(new Aabb3i(m - new Int3(1, 0, 1), m + new Int3(2, 4, 2))));
    }

    [Fact]
    public void ObjectAndLightBudgetsPerChunk()
    {
        var g = DungeonGenerationTests.Loaded();
        var content = g.Content;
        var objects = new Dictionary<Int3, int>();
        var lights = new Dictionary<Int3, int>();
        var spikes = new Dictionary<Int3, int>();
        foreach (var op in Objects(DungeonGenerationTests.Feature(g)))
        {
            var def = content.TileObjects.Get(op.ObjectId);
            var c = ChunkMath.CellToChunk(op.Anchor);
            var counter = def.Hazard != null ? spikes : objects;
            counter[c] = counter.GetValueOrDefault(c) + 1;
            if (def.Light is { } l && (l[0] | l[1] | l[2]) != 0) lights[c] = lights.GetValueOrDefault(c) + 1;
        }
        Assert.All(objects.Values, n => Assert.True(n <= DungeonDecorator.ObjectBudget));
        Assert.All(lights.Values, n => Assert.True(n <= DungeonDecorator.LightBudget));
        Assert.All(spikes.Values, n => Assert.True(n <= DungeonDecorator.SpikeBudget));
    }

    [Fact]
    public void PlannedWiresJoinEachPlateToItsTrap()
    {
        var g = DungeonGenerationTests.Loaded();
        var plan = g.World.Session.Generator.Plan;
        var ops = Objects(DungeonGenerationTests.Feature(g));
        var plates = ops.Where(o => o.ObjectId == "pressure_plate").ToList();
        var traps = ops.Where(o => o.ObjectId == "dart_trap").Select(o => o.Anchor).ToHashSet();
        Assert.NotEmpty(plates);
        Assert.Equal(plates.Count, traps.Count);
        Assert.True(plan.PlannedWires.IsSubsetOf(g.World.Session.Wires), "a new world starts with the planned wires");
        foreach (var plate in plates)
        {
            var start = plate.Anchor + Int3.Down;
            Assert.Contains(start, plan.PlannedWires);
            var seen = new HashSet<Int3> { start };
            var queue = new Queue<Int3>(seen);
            int reached = 0;
            while (queue.Count > 0)
            {
                var c = queue.Dequeue();
                if (traps.Contains(c)) reached++;
                foreach (var o in Faces.Offsets)
                    if (plan.PlannedWires.Contains(c + o) && seen.Add(c + o)) queue.Enqueue(c + o);
            }
            Assert.Equal(1, reached);
        }
    }

    [Fact]
    public void SteppingOnADungeonPlateFiresItsDart()
    {
        // Its own world: ticking moves the streamer, which must not disturb the shared installed dungeon.
        using var g = TestGame.Create(seed: 1234);
        g.World.Spawner.Enabled = false;
        g.World.Town.ArrivalsEnabled = false;
        var session = g.World.Session;
        var plate = Objects(session.Generator.Plan.Features.All.OfType<DungeonFeature>().Single()).First(o => o.ObjectId == "pressure_plate");
        var chunk = ChunkMath.CellToChunk(plate.Anchor);
        for (int y = -1; y <= 1; y++)
            for (int z = -1; z <= 1; z++)
                for (int x = -1; x <= 1; x++)
                {
                    var c = chunk + new Int3(x, y, z);
                    if (session.World.ChunkInBounds(c) && !session.World.HasChunk(c)) session.Install(session.Produce(c));
                }
        g.Player.Position = TestGame.CellFeet(plate.Anchor);
        g.Player.Velocity = System.Numerics.Vector3.Zero;
        int before = g.World.Projectiles.All.Count(p => p.Def.Id == "trap_dart");
        g.Ticks(2);
        Assert.True(g.World.Projectiles.All.Count(p => p.Def.Id == "trap_dart") > before, "the dart trap did not fire");
    }
}
