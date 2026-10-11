using System.Numerics;
using Strata.Core.Common;
using Strata.Core.Content.Defs;
using Strata.Core.Gameplay.Building;
using Strata.Core.Gameplay.Navigation;
using Strata.Core.Tests.Support;
using Strata.Core.World;
using Strata.Core.WorldGen;
using Strata.Core.WorldGen.Plan;
using Strata.Core.WorldGen.Structures;
using Strata.Core.WorldGen.Structures.Dungeon;

namespace Strata.Core.Tests.WorldGen;

/// <summary>The Dungeon on the Phase 1 relief across seeds: the keep stands free on level ground, the gate is out in
/// the open on the way in from the world centre, the Old Man waits there, and (on the hilly seeds) every room is
/// reachable on foot from the gate with nothing buried or floating.</summary>
public class DungeonSiteTests
{
    public static IEnumerable<object[]> Seeds() => Enumerable.Range(1, 12).Select(s => new object[] { s });

    private static (TestGame Game, DungeonFeature Dungeon, Int3 Gate) Create(int seed, bool whole)
    {
        var g = TestGame.Create(seed: seed);
        g.World.Spawner.Enabled = false;
        g.World.Town.ArrivalsEnabled = false;
        var session = g.World.Session;
        var plan = session.Generator.Plan;
        var f = plan.Features.All.OfType<DungeonFeature>().Single();
        var gate = plan.Landmarks[LandmarkIds.DungeonGate];
        var chunks = new HashSet<Int3>();
        void Add(Aabb3i box)
        {
            var lo = ChunkMath.CellToChunk(box.Min - Int3.One * 3);
            var hi = ChunkMath.CellToChunk(box.Max + Int3.One * 3);
            for (int y = lo.Y; y <= hi.Y; y++)
                for (int z = lo.Z; z <= hi.Z; z++)
                    for (int x = lo.X; x <= hi.X; x++) chunks.Add(new Int3(x, y, z));
        }
        Add(new Aabb3i(gate - new Int3(48, 24, 48), gate + new Int3(48, 24, 48)));
        Add(f.Layout.KeepInterior.Expand(10));
        if (whole)
        {
            foreach (var h in f.Layout.Halls) Add(h.Bounds);
            foreach (var r in f.Layout.Rooms) Add(r.Interior.Expand(8));
        }
        foreach (var c in chunks)
            if (session.World.ChunkInBounds(c) && !session.World.HasChunk(c)) session.Install(session.Produce(c));
        return (g, f, gate);
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void TheKeepStandsFreeOnTheGroundWithTheGateOutside(int seed)
    {
        var (g, f, gate) = Create(seed, whole: false);
        using var _ = g;
        var l = f.Layout;
        var w = g.World.Session.World;
        var terrain = g.World.Session.Generator.Terrain;
        var nav = g.World.Navigator;
        var k = l.KeepInterior;
        var shaft = l.Halls[0].Interior(0).Expand(2);
        int ring = 0, buried = 0;
        for (int x = k.Min.X - 8; x < k.Max.X + 8; x++)
            for (int z = k.Min.Z - 8; z < k.Max.Z + 8; z++)
            {
                if (x < k.Max.X + 6 && x >= k.Min.X - 6 && z < k.Max.Z + 6 && z >= k.Min.Z - 6 && !shaft.Contains(x, shaft.Min.Y, z))
                {
                    // Grounded: solid from the floor down to the ground under the walls and the apron.
                    int ground = (int)MathF.Floor(terrain.SurfaceHeight(x, z));
                    for (int y = k.Min.Y - 1; y >= Math.Min(ground, k.Min.Y - 1); y--)
                        Assert.True(w.IsSolid(x, y, z), $"seed {seed}: air under the keep at ({x},{y},{z})");
                }
                // Not buried: just outside the walls the ground is cleared below the slit windows.
                int dx = Math.Max(k.Min.X - x, x - (k.Max.X - 1)), dz = Math.Max(k.Min.Z - z, z - (k.Max.Z - 1));
                if (Math.Max(dx, dz) < 6) continue;
                ring++;
                if (w.IsSolid(x, k.Min.Y + 4, z)) buried++;
            }
        // The turrets and the approach's brick stand in the ring; the hillside never does.
        Assert.True(buried <= 24, $"seed {seed}: the keep is buried in {buried} of {ring} columns round it");

        Assert.True(nav.IsStandable(gate, NavAgent.Humanoid), $"seed {seed}: the gate {gate} is not standable");
        Assert.DoesNotContain("dungeon", g.World.Session.ZonesAt(gate));
        var door = nav.NearestStandable(l.Door - l.Inward, NavAgent.Humanoid, 3);
        Assert.True(door != null && nav.FindPath(gate, door.Value, NavAgent.Humanoid, 20000) != null, $"seed {seed}: the door is not reachable from the gate");
        // The way in from the world centre: 30 cells further inland on the open surface.
        var outer = gate + l.Inward * 30;
        var from = nav.NearestStandable(new Int3(outer.X, (int)MathF.Floor(terrain.SurfaceHeight(outer.X, outer.Z)) + 1, outer.Z), NavAgent.Humanoid, 6);
        Assert.True(from != null && nav.FindPath(from.Value, gate, NavAgent.Humanoid, 60000) != null, $"seed {seed}: the gate is not reachable from inland");

        g.World.Town.UpdateLandmarks();
        var keeper = g.World.Town.Find("old_man");
        Assert.True(keeper != null && Vector3.Distance(keeper.Position, TestGame.CellFeet(gate)) < 5f, $"seed {seed}: no Old Man at the gate");

        var again = new WorldGenerator(TestPaths.Content, new WorldSettings { Seed = seed, Size = WorldSizePreset.Tiny });
        Assert.Equal(l.Hash(), again.Plan.Features.All.OfType<DungeonFeature>().Single().Layout.Hash());
    }

    /// <summary>Seeds 4 and 11 put the keep's first site on a hillside (the flattest site still needs the clearing).</summary>
    [Theory]
    [InlineData(4)]
    [InlineData(11)]
    public void EveryRoomIsReachableFromTheGateAndNothingFloats(int seed)
    {
        var (g, f, gate) = Create(seed, whole: true);
        using var _ = g;
        var w = g.World.Session.World;
        var nav = g.World.Navigator;
        foreach (var room in f.Layout.Rooms.Where(r => r.Kind != RoomKind.Keep))
        {
            var goal = nav.NearestStandable(room.CenterFloor, NavAgent.Humanoid, 3);
            Assert.True(goal != null, $"seed {seed}: room {room.Id} has no standable floor");
            Assert.True(nav.FindPath(gate, goal!.Value, NavAgent.Humanoid, 400000) != null, $"seed {seed}: room {room.Id} ({room.Kind}) is unreachable from the gate");
        }
        var cells = new HashSet<Int3>();
        foreach (var op in f.Blueprint.Ops.OfType<ObjectOp>())
        {
            for (int y = op.Bounds.Min.Y; y < op.Bounds.Max.Y; y++)
                for (int z = op.Bounds.Min.Z; z < op.Bounds.Max.Z; z++)
                    for (int x = op.Bounds.Min.X; x < op.Bounds.Max.X; x++)
                        Assert.True(cells.Add(new Int3(x, y, z)), $"seed {seed}: {op.ObjectId}@{op.Anchor} overlaps");
            var obj = w.TileObjects.At(op.Anchor);
            Assert.True(obj != null && obj.Def.Id == op.ObjectId, $"seed {seed}: {op.ObjectId}@{op.Anchor} is missing");
            Assert.True(PlacementRules.IsSupported(w, obj!), $"seed {seed}: {obj} floats");
            var b = obj!.Bounds;
            for (int y = b.Min.Y; y < b.Max.Y; y++)
                for (int z = b.Min.Z; z < b.Max.Z; z++)
                    for (int x = b.Min.X; x < b.Max.X; x++) Assert.False(w.IsSolid(x, y, z), $"seed {seed}: {obj} is buried");
            if (obj.Def.Anchor == TileAnchor.Floor && obj.Def.Id != "candle")
                for (int z = b.Min.Z; z < b.Max.Z; z++)
                    for (int x = b.Min.X; x < b.Max.X; x++) Assert.True(w.IsSolid(x, b.Min.Y - 1, z), $"seed {seed}: {obj} overhangs");
        }
    }
}
