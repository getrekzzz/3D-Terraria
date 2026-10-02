using System.Numerics;
using Strata.Core.Common;
using Strata.Core.Gameplay.Navigation;
using Strata.Core.Meshing;
using Strata.Core.Tests.Support;
using Strata.Core.Voxels;
using Strata.Core.WorldGen.Structures;
using Strata.Core.WorldGen.Structures.Dungeon;

namespace Strata.Core.Tests.WorldGen;

/// <summary>The carved Dungeon of seed 1234 (Tiny): shells, panels, stairs, shafts, rooms and crossings, checked in the
/// generated and installed world.</summary>
public class DungeonGenerationTests
{
    private static readonly Lazy<TestGame> Game = new(() =>
    {
        var g = TestGame.Create(seed: 1234);
        g.World.Spawner.Enabled = false;
        g.World.Town.ArrivalsEnabled = false;
        return g;
    });

    private static readonly object Gate = new();
    private static bool _loaded;

    /// <summary>Installs every chunk that holds part of the dungeon (once; shared by the tests in this class).</summary>
    internal static TestGame Loaded()
    {
        var g = Game.Value;
        lock (Gate)
        {
            if (_loaded) return g;
            var session = g.World.Session;
            var l = Feature(g).Layout;
            var chunks = new HashSet<Int3>();
            void Add(Aabb3i box)
            {
                var lo = ChunkMath.CellToChunk(box.Min - Int3.One * 3);
                var hi = ChunkMath.CellToChunk(box.Max + Int3.One * 3);
                for (int y = lo.Y; y <= hi.Y; y++)
                    for (int z = lo.Z; z <= hi.Z; z++)
                        for (int x = lo.X; x <= hi.X; x++) chunks.Add(new Int3(x, y, z));
            }
            foreach (var h in l.Halls) Add(h.Bounds);
            foreach (var r in l.Rooms) Add(r.Interior.Expand(8));
            foreach (var c in chunks)
                if (session.World.ChunkInBounds(c) && !session.World.HasChunk(c)) session.Install(session.Produce(c));
            _loaded = true;
        }
        return g;
    }

    internal static DungeonFeature Feature(TestGame g) =>
        g.World.Session.Generator.Plan.Features.All.OfType<DungeonFeature>().Single();

    private static VoxelWorld World(TestGame g) => g.World.Session.World;

    private static bool Air(TestGame g, Int3 c) => !World(g).IsSolid(c.X, c.Y, c.Z);

    private static bool InAnyInterior(DungeonLayout l, Int3 c) =>
        l.Halls.Any(h => h.Interiors().Any(b => b.Contains(c))) || l.Rooms.Any(r => r.Interior.Contains(c) || r.Doorways.Any(d => d.Box.Contains(c)));

    [Fact]
    public void HallsAreCarvedAndLinedWithProtectedBrick()
    {
        var g = Loaded();
        var l = Feature(g).Layout;
        var w = World(g);
        foreach (var h in l.Halls)
        {
            var box = h.Interior(h.Kind == HallKind.Stairs ? h.Length / 2 : 0);
            var mid = new Int3((box.Min.X + box.Max.X) / 2, h.Kind == HallKind.Shaft ? (box.Min.Y + box.Max.Y) / 2 : box.Min.Y + 2, (box.Min.Z + box.Max.Z) / 2);
            if (h.Kind == HallKind.Shaft && w.GetShape(mid.X, mid.Y, mid.Z) != 0) mid += Int3.Up; // a platform layer
            Assert.True(Air(g, mid), $"hall {h.Id} ({h.Kind}) is not carved at {mid}");
            var across = h.Kind == HallKind.Shaft || h.Dir.X != 0 ? new Int3(0, 0, 1) : new Int3(1, 0, 0);
            foreach (int side in new[] { -1, 1 })
            {
                // Out from the centre line to the first wall cell, then two more: panel, then brick shell.
                var c = mid;
                while (box.Contains(c)) c += across * side;
                for (int k = 0; k < 2; k++, c += across * side)
                {
                    if (InAnyInterior(l, c)) break; // a crossing opens the wall here
                    Assert.False(Air(g, c), $"hall {h.Id} wall missing at {c}");
                    Assert.True((w.GetFlags(c.X, c.Y, c.Z) & CellFlags.Structure) != 0, $"hall {h.Id} wall at {c} is not protected");
                }
            }
        }
        // Halls and rooms are lined with this dungeon's panels.
        var style = Feature(g).Style;
        var room = l.Rooms.First(r => r.Kind != RoomKind.Keep && r.Interior.Size.X > 8);
        var face = new Int3(room.Interior.Min.X - 1, room.Interior.Min.Y + 1, room.Interior.Min.Z + 1);
        if (!InAnyInterior(l, face)) Assert.Equal(style.Panel(room.PanelStyle), g.Content.Blocks[w.GetBlock(face)].Id);
    }

    [Fact]
    public void ShaftsHaveAPlatformEveryFourCells()
    {
        var g = Loaded();
        var l = Feature(g).Layout;
        var w = World(g);
        int shafts = 0;
        foreach (var h in l.Halls.Where(h => h.Kind == HallKind.Shaft))
        {
            shafts++;
            var box = h.Interior(0);
            int previous = h.End.Y; // standing height (feet) of the last surface climbed from
            for (int y = h.End.Y; y < h.Start.Y; y++)
            {
                bool platform = false;
                for (int x = box.Min.X; x < box.Max.X && !platform; x++)
                    for (int z = box.Min.Z; z < box.Max.Z && !platform; z++)
                        platform = ShapeCode.Shape(w.GetShape(x, y, z)) == CellShape.Platform && w.GetBlock(new Int3(x, y, z)) != 0;
                if (!platform) continue;
                Assert.True(y + 1 - previous <= DungeonBuilder.PlatformSpacing, $"shaft {h.Id}: a {y + 1 - previous}-cell climb to the platform at {y}");
                previous = y + 1;
            }
            Assert.True(h.Start.Y - previous <= DungeonBuilder.PlatformSpacing, $"shaft {h.Id}: the top platform is {h.Start.Y - previous} cells under the floor it leads to");
        }
        Assert.True(shafts >= 1);
    }

    [Fact]
    public void StairsTurnTheirTallHalfUphill()
    {
        // The rotation mapping matches the mesher: the Stair's upper box sits on the rotation side of the cell.
        foreach (var dir in new[] { new Int3(1, 0, 0), new Int3(-1, 0, 0), new Int3(0, 0, 1), new Int3(0, 0, -1) })
        {
            var polys = ShapeGeometry.Get(ShapeCode.Pack(CellShape.Stair, DungeonBuilder.RotationToward(dir)));
            var top = polys.Where(p => p.Points.All(v => v.Y > 0.99f)).SelectMany(p => p.Points).Aggregate(Vector3.Zero, (a, v) => a + v);
            var centre = top / polys.Where(p => p.Points.All(v => v.Y > 0.99f)).Sum(p => p.Points.Length);
            Assert.True(Vector3.Dot(centre - new Vector3(0.5f, 1f, 0.5f), new Vector3(dir.X, 0, dir.Z)) > 0.2f, $"rotation for {dir}");
        }
        var g = Loaded();
        var l = Feature(g).Layout;
        var w = World(g);
        foreach (var h in l.Halls.Where(h => h.Kind == HallKind.Stairs))
        {
            var step = h.Interior(1);
            var ground = new Int3((step.Min.X + step.Max.X) / 2, step.Min.Y - 1, (step.Min.Z + step.Max.Z) / 2);
            byte code = w.GetShape(ground.X, ground.Y, ground.Z);
            Assert.Equal(CellShape.Stair, ShapeCode.Shape(code));
            Assert.Equal(DungeonBuilder.RotationToward(-h.Dir), ShapeCode.Rotation(code));
        }
    }

    [Fact]
    public void StairsAreWalkable()
    {
        var g = Loaded();
        var l = Feature(g).Layout;
        int checkedStairs = 0;
        foreach (var h in l.Halls.Where(h => h.Kind == HallKind.Stairs && h.Length >= 4))
        {
            var top = Snap(g, h.Start + h.Dir * 1 + Int3.Down);
            var bottom = Snap(g, h.End - h.Dir * 1 + Int3.Up);
            Assert.True(top != null && bottom != null, $"stairs {h.Id} have no standable ends");
            Assert.NotNull(g.World.Navigator.FindPath(top!.Value, bottom!.Value, NavAgent.Humanoid, 20000));
            Assert.NotNull(g.World.Navigator.FindPath(bottom.Value, top.Value, NavAgent.Humanoid, 20000)); // and back up
            checkedStairs++;
        }
        Assert.True(checkedStairs >= 1);
    }

    /// <summary>Downhill on foot: stairs, and shafts stepped down platform by platform (climbing back up jumps from
    /// platform to platform, which the navigator does not model; ShaftsHaveAPlatformEveryFourCells covers it).</summary>
    private static readonly NavAgent Explorer = NavAgent.Humanoid;

    [Fact]
    public void RoomsAreReachableOnFoot()
    {
        var g = Loaded();
        var l = Feature(g).Layout;
        var start = Snap(g, l.Nodes[1].Floor + Int3.Up * 0)!.Value;
        var targets = new List<DungeonRoom>();
        if (l.VaultRoom >= 0) targets.Add(l.Rooms[l.VaultRoom]);
        if (l.MechanicRoom >= 0) targets.Add(l.Rooms[l.MechanicRoom]);
        targets.AddRange(l.Rooms.Where(r => r.Kind is RoomKind.Library or RoomKind.Treasure or RoomKind.Barracks or RoomKind.SpikePit).Take(3 - Math.Min(2, targets.Count)));
        Assert.True(targets.Count >= 3);
        foreach (var room in targets)
        {
            var goal = Snap(g, room.CenterFloor);
            Assert.True(goal != null, $"room {room.Id} has no standable floor");
            var path = g.World.Navigator.FindPath(start, goal!.Value, Explorer, 400000);
            Assert.True(path != null, $"room {room.Id} ({room.Kind}) at {goal} is unreachable from the entrance junction {start}");
        }
    }

    [Fact]
    public void CrossingsStayOpen()
    {
        var g = Loaded();
        var l = Feature(g).Layout;
        foreach (var room in l.Rooms)
            foreach (var d in room.Doorways)
            {
                // Through the cut: its centre column (over the floor step for a wall doorway, all of it through a ceiling).
                var c = new Int3((d.Box.Min.X + d.Box.Max.X) / 2, d.Box.Min.Y, (d.Box.Min.Z + d.Box.Max.Z) / 2);
                for (int y = d.Normal.Y == 0 ? 1 : 0; y < Math.Min(d.Box.Size.Y, 4); y++)
                    Assert.True(Air(g, c + Int3.Up * y), $"room {room.Id} doorway {d.Box} is blocked at {c + Int3.Up * y}");
            }
        foreach (var h in l.Halls.Where(h => h.Kind != HallKind.Shaft))
            foreach (var box in new[] { h.Interior(0), h.Interior(h.InteriorCount - 1) })
            {
                var c = new Int3((box.Min.X + box.Max.X) / 2, box.Min.Y + 1, (box.Min.Z + box.Max.Z) / 2);
                Assert.True(Air(g, c) && Air(g, c + Int3.Up * 3), $"hall {h.Id} is closed where it meets its node at {c}");
            }
    }

    [Fact]
    public void TheKeepStandsOnTheGround()
    {
        var g = Loaded();
        var l = Feature(g).Layout;
        var w = World(g);
        var terrain = g.World.Session.Generator.Terrain;
        var k = l.KeepInterior;
        var shaft = l.Halls[0].Interior(0).Expand(2);
        int floor = k.Min.Y, columns = 0;
        // Every column under the walls and the foundation's apron is solid from the floor down to the ground.
        for (int x = k.Min.X - 6; x < k.Max.X + 6; x++)
            for (int z = k.Min.Z - 6; z < k.Max.Z + 6; z++)
            {
                if (shaft.Contains(x, shaft.Min.Y, z)) continue;
                int ground = (int)MathF.Floor(terrain.SurfaceHeight(x, z));
                for (int y = floor - 1; y >= Math.Min(ground, floor - 1); y--)
                    Assert.True(w.IsSolid(x, y, z), $"air under the keep at ({x},{y},{z}), ground {ground}, floor {floor}");
                columns++;
            }
        Assert.True(columns > 500);
        // The roof and the turrets rise over the walls.
        var corner = new Int3(k.Min.X - 3, floor + 22, k.Min.Z - 3);
        Assert.True(w.IsSolid(corner.X, corner.Y, corner.Z), "no turret at the keep's corner");
    }

    [Fact]
    public void TheDoorFacesTheWorldCentre()
    {
        var g = Loaded();
        var l = Feature(g).Layout;
        var size = g.World.Session.Generator.Plan.Size;
        var k = l.KeepInterior;
        var centre = new Int3((k.Min.X + k.Max.X) / 2, k.Min.Y, (k.Min.Z + k.Max.Z) / 2);
        var toWorld = new Int3(size.X / 2 - centre.X, 0, size.Z / 2 - centre.Z);
        Assert.True(l.Inward.X * toWorld.X + l.Inward.Z * toWorld.Z > 0, "the door faces away from the world centre");
        Assert.True(l.Door.X * l.Inward.X + l.Door.Z * l.Inward.Z > centre.X * l.Inward.X + centre.Z * l.Inward.Z);
        // Open through the 5-cell front wall, 9 tall, and out onto the approach.
        for (int a = 0; a < 9; a++)
            for (int y = 0; y < 8; y++)
                Assert.True(Air(g, l.Door + l.Inward * a + Int3.Up * y), $"door blocked at {l.Door + l.Inward * a + Int3.Up * y}");
        var gate = g.World.Session.Generator.Plan.Landmarks[Strata.Core.WorldGen.Plan.LandmarkIds.DungeonGate];
        Assert.True(g.World.Navigator.IsStandable(gate, NavAgent.Humanoid), $"the gate {gate} is not standable");
        Assert.DoesNotContain("dungeon", g.World.Session.ZonesAt(gate));
        Assert.Contains("dungeon", g.World.Session.ZonesAt(centre + Int3.Up * 2));
        Assert.NotNull(g.World.Navigator.FindPath(gate, Snap(g, l.Door + Int3.Up * 0 - l.Inward)!.Value, NavAgent.Humanoid, 20000));
    }

    [Fact]
    public void ExactlyOneShadowKeyChestAndEveryChestLocked()
    {
        var g = Game.Value;
        var chests = Feature(g).Blueprint.Ops.OfType<ObjectOp>().Where(o => o.ObjectId == "gold_chest").ToList();
        Assert.NotEmpty(chests);
        Assert.Single(chests, o => o.Loot == "dungeon_chest_shadow_key");
        Assert.All(chests, o => Assert.True(o.Locked, $"chest at {o.Anchor} is not locked"));
    }

    [Fact]
    public void UnderworldChestsAreLockedShadowChests()
    {
        var plan = Game.Value.World.Session.Generator.Plan;
        var ops = plan.Features.All.OfType<StructureFeature>().Where(f => f is not DungeonFeature)
            .SelectMany(f => f.Blueprint.Ops.OfType<ObjectOp>()).Where(o => o.Anchor.Y < plan.Bands.UnderworldTop).ToList();
        var shadow = ops.Where(o => o.ObjectId == "shadow_chest").ToList();
        Assert.NotEmpty(shadow);
        Assert.All(shadow, o => Assert.True(o.Locked && o.Loot == "shadow_chest"));
        Assert.DoesNotContain(ops, o => o.ObjectId == "gold_chest" && o.Loot == "cabin_chest");
    }

    private static Int3? Snap(TestGame g, Int3 cell) => g.World.Navigator.NearestStandable(cell, NavAgent.Humanoid, 3);
}
