using System.Collections.Concurrent;
using Strata.Core.Common;
using Strata.Core.Determinism;
using Strata.Core.Tests.Support;
using Strata.Core.World;
using Strata.Core.WorldGen;
using Strata.Core.WorldGen.Plan;
using Strata.Core.WorldGen.Structures.Dungeon;

namespace Strata.Core.Tests.WorldGen;

public class DungeonLayoutTests
{
    private static readonly ConcurrentDictionary<(int, WorldSizePreset), (DungeonSite Site, DungeonLayout Layout)> Cache = new();

    /// <summary>A synthetic continent: rolling ground around 2/3 of the height, land out to ~0.95 of the land radius,
    /// the gate near the coast on one of four sides (by seed) as the dungeon pass would place it.</summary>
    internal static DungeonSite SyntheticSite(int seed, WorldSizePreset size)
    {
        var dims = WorldSettings.DimensionsOf(size);
        var bands = new LayerBands(dims.Y);
        int cx = dims.X / 2, cz = dims.Z / 2;
        float radius = Math.Min(dims.X, dims.Z) * 0.42f;
        int baseY = bands.SeaLevel + 6 + seed % 5;
        Func<int, int, int> surface = (x, z) => baseY + Tri(x + seed * 37, 96) / 6 + Tri(z + seed * 11, 80) / 6 + Tri(x + z, 40) / 10;
        Func<int, int, float> landness = (x, z) =>
        {
            float d = MathF.Sqrt((float)(x - cx) * (x - cx) + (float)(z - cz) * (z - cz));
            return Math.Clamp((radius * 1.05f - d) / (radius * 0.15f), 0f, 1f);
        };
        var inward = (seed % 4) switch { 0 => new Int3(1, 0, 0), 1 => new Int3(-1, 0, 0), 2 => new Int3(0, 0, 1), _ => new Int3(0, 0, -1) };
        int dist = (int)(radius * 0.86f), lateral = (seed * 29 % 41) - 20;
        int gx = cx - inward.X * dist + (inward.X == 0 ? lateral : 0), gz = cz - inward.Z * dist + (inward.Z == 0 ? lateral : 0);
        return new DungeonSite { Gate = new Int3(gx, surface(gx, gz), gz), Inward = inward, WorldSize = dims, Bands = bands, Surface = surface, Landness = landness };
    }

    private static int Tri(int v, int period) => Math.Abs(((v % period) + period) % period - period / 2);

    private static (DungeonSite Site, DungeonLayout Layout) Get(int seed, WorldSizePreset size) => Cache.GetOrAdd((seed, size), key =>
    {
        var site = SyntheticSite(key.Item1, key.Item2);
        return (site, DungeonPlanner.Plan(site, DungeonBudget.For(key.Item2), SeedTree.FromWorldSeed(key.Item1).Child("dungeon")));
    });

    public static IEnumerable<object[]> Cases()
    {
        foreach (var size in new[] { WorldSizePreset.Tiny, WorldSizePreset.Small })
            for (int seed = 1; seed <= 24; seed++) yield return new object[] { seed, size };
    }

    private static IEnumerable<Aabb3i> AllInteriors(DungeonLayout l, bool includeEntrance)
    {
        foreach (var h in l.Halls)
            if (includeEntrance || h.Id != 0)
                foreach (var b in h.Interiors()) yield return b;
        foreach (var r in l.Rooms)
            if (includeEntrance || r.Id > 1) yield return r.Interior;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void SameSeedSameLayout(int seed, WorldSizePreset size)
    {
        var site = SyntheticSite(seed, size);
        var again = DungeonPlanner.Plan(site, DungeonBudget.For(size), SeedTree.FromWorldSeed(seed).Child("dungeon"));
        Assert.Equal(Get(seed, size).Layout.Hash(), again.Hash());
        Assert.NotEqual(Get(seed % 24 + 1, size).Layout.Hash(), again.Hash());
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void EveryRoomIsReachableFromTheKeep(int seed, WorldSizePreset size)
    {
        var l = Get(seed, size).Layout;
        var seen = new bool[l.Nodes.Count];
        var queue = new Queue<int>();
        queue.Enqueue(0);
        seen[0] = true;
        while (queue.Count > 0)
        {
            int n = queue.Dequeue();
            foreach (var e in l.Edges)
            {
                int other = e.A == n ? e.B : e.B == n ? e.A : -1;
                if (other >= 0 && !seen[other]) { seen[other] = true; queue.Enqueue(other); }
            }
        }
        foreach (var r in l.Rooms) Assert.True(seen[r.Node], $"room {r.Id} ({r.Kind}) unreachable");
        // The graph matches the geometry: halls start in (or against) their node box and end inside the next one.
        foreach (var h in l.Halls)
        {
            Assert.True(h.Interior(0).Intersects(l.Rooms[l.Nodes[h.FromNode].Room].Interior.Expand(1)), $"hall {h.Id} start");
            Assert.True(h.Interior(h.InteriorCount - 1).Intersects(l.Rooms[l.Nodes[h.ToNode].Room].Interior), $"hall {h.Id} end");
        }
        foreach (var e in l.Edges.Where(e => e.SideDoor))
        {
            var room = l.Rooms[l.Nodes[e.B].Room];
            var door = room.Doorways.Single(d => d.Hall == e.Hall);
            Assert.True(door.Box.Intersects(room.Interior.Expand(1)) && door.Box.Intersects(l.Halls[e.Hall].Interior(0).Expand(1)));
            Assert.Equal(5, door.Box.Size.Y);
            Assert.Equal((2, 3), (Math.Min(door.Box.Size.X, door.Box.Size.Z), Math.Max(door.Box.Size.X, door.Box.Size.Z)));
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void StaysInsideTheWorldAndUnderground(int seed, WorldSizePreset size)
    {
        var (site, l) = Get(seed, size);
        Assert.True(l.Bounds.Min.X >= 10 && l.Bounds.Min.Z >= 10 && l.Bounds.Max.X <= site.WorldSize.X - 10 && l.Bounds.Max.Z <= site.WorldSize.Z - 10);
        var keepZone = l.KeepInterior.Expand(8);
        foreach (var b in AllInteriors(l, includeEntrance: false))
            foreach (var (x, z) in new[] { (b.Min.X, b.Min.Z), (b.Max.X - 1, b.Min.Z), (b.Min.X, b.Max.Z - 1), (b.Max.X - 1, b.Max.Z - 1) })
            {
                if (keepZone.Contains(x, keepZone.Min.Y, z)) continue;
                // At least the 2-cell shell plus a few cells of natural rock everywhere (12 at the sampled points).
                Assert.True(b.Max.Y - 1 <= site.Surface(x, z) - 6, $"{b} breaks the surface at ({x},{z})");
            }
        Assert.Equal(l.KeepInterior.Min.Y, site.Gate.Y + 1);
        Assert.False(l.KeepInterior.Contains(l.Door));
        Assert.True(l.KeepInterior.Contains(l.Door - l.Inward));
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void NeverReachesBelowItsFloor(int seed, WorldSizePreset size)
    {
        var (site, l) = Get(seed, size);
        Assert.Equal(DungeonBudget.For(size).FloorY(site.Bands), l.FloorLimit);
        Assert.True(l.FloorLimit >= site.Bands.UnderworldTop + 40);
        foreach (var b in AllInteriors(l, includeEntrance: true)) Assert.True(b.Min.Y >= l.FloorLimit, $"{b} below {l.FloorLimit}");
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void MeetsItsQuotas(int seed, WorldSizePreset size)
    {
        var l = Get(seed, size).Layout;
        var budget = DungeonBudget.For(size);
        int rooms = l.Rooms.Count(r => r.Kind is not (RoomKind.Keep or RoomKind.Junction or RoomKind.Alcove));
        Assert.True(rooms >= budget.MinRooms, $"{rooms} rooms < {budget.MinRooms} (trunk {l.TrunkCells}, branches {l.BranchCells})");
        Assert.True(l.ChestSpots.Count >= budget.LockedChests, $"{l.ChestSpots.Count} chest spots");
        Assert.Single(l.ChestSpots, s => s.ShadowKey);
        Assert.Single(l.Rooms, r => r.Kind == RoomKind.Vault);
        Assert.Single(l.Rooms, r => r.Kind == RoomKind.Mechanic);
        Assert.Contains(l.Rooms, r => r.Kind == RoomKind.Library);
        Assert.Equal(RoomKind.Vault, l.Rooms[l.VaultRoom].Kind);
        Assert.Equal(RoomKind.Mechanic, l.Rooms[l.MechanicRoom].Kind);
        Assert.True(l.Rooms[l.MechanicRoom].Interior.Contains(l.MechanicCell));
        var vault = l.Rooms[l.VaultRoom].Interior;
        Assert.True(vault.Size.X >= 14 && vault.Size.Z >= 14);
        var sideNodes = l.Edges.Where(e => e.SideDoor).Select(e => e.B).ToHashSet();
        Assert.Equal(l.Rooms.Where(r => r.Kind is not (RoomKind.Keep or RoomKind.Junction) && !sideNodes.Contains(r.Node)).Min(r => r.Interior.Min.Y), vault.Min.Y);
        Assert.Equal(6, l.VaultChestSpots.Count);
        Assert.Equal(6, l.VaultChestSpots.Select(s => s.Cell).Distinct().Count());
        foreach (var s in l.VaultChestSpots) Assert.True(vault.Contains(s.Cell) && (vault.Contains(s.Cell + new Int3(1, 0, 0)) || vault.Contains(s.Cell + new Int3(0, 0, 1))));
        foreach (var s in l.ChestSpots)
        {
            Assert.True(l.Rooms[s.Room].Interior.Contains(s.Cell), $"chest {s.Cell} outside room {s.Room}");
            Assert.Equal(l.Rooms[s.Room].Interior.Min.Y, s.Cell.Y);
        }
        Assert.Equal(l.ChestSpots.Count, l.ChestSpots.Select(s => s.Cell).Distinct().Count());
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void StairsDescendOneCellPerStep(int seed, WorldSizePreset size)
    {
        var l = Get(seed, size).Layout;
        foreach (var h in l.Halls.Where(h => h.Kind == HallKind.Stairs))
        {
            Assert.Equal(h.Length + 1, h.InteriorCount);
            for (int k = 0; k < h.InteriorCount; k++)
            {
                var b = h.Interior(k);
                Assert.Equal(h.Start.Y - k, b.Min.Y);
                Assert.Equal(h.FloorY(k), b.Min.Y);
                Assert.True(b.Contains(new Int3(h.Start.X + h.Dir.X * k, b.Min.Y, h.Start.Z + h.Dir.Z * k)));
                Assert.Equal(1, h.Dir.X != 0 ? b.Size.X : b.Size.Z);
            }
            Assert.Equal(h.End, new Int3(h.Start.X + h.Dir.X * h.Length, h.Start.Y - h.Length, h.Start.Z + h.Dir.Z * h.Length));
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void HallsHaveHeadroom(int seed, WorldSizePreset size)
    {
        var l = Get(seed, size).Layout;
        foreach (var h in l.Halls)
        {
            Assert.True(h.Height >= 8);
            foreach (var b in h.Interiors()) Assert.True(b.Size.Y >= 8 && Math.Min(b.Size.X, b.Size.Z) >= (h.Kind == HallKind.Stairs ? 1 : 7));
        }
        foreach (var r in l.Rooms) Assert.True(r.Interior.Size.Y >= 8);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void InteriorsOnlyMeetWhereJoined(int seed, WorldSizePreset size)
    {
        var l = Get(seed, size).Layout;
        var pieces = new List<(Aabb3i[] Boxes, HashSet<int> Nodes, int Hall)>();
        foreach (var h in l.Halls) pieces.Add((h.Interiors(), new HashSet<int> { h.FromNode, h.ToNode }, h.Id));
        foreach (var r in l.Rooms) pieces.Add((new[] { r.Interior }, new HashSet<int> { r.Node }, -1));
        for (int i = 0; i < pieces.Count; i++)
            for (int j = i + 1; j < pieces.Count; j++)
            {
                var shared = pieces[i].Nodes.Intersect(pieces[j].Nodes).ToList();
                bool hallAndItsBox = shared.Count > 0 && (pieces[i].Hall < 0 || pieces[j].Hall < 0);
                if (hallAndItsBox) continue;
                foreach (var a in pieces[i].Boxes)
                    foreach (var b in pieces[j].Boxes)
                    {
                        if (!a.Intersects(b)) continue;
                        // Two halls leaving the same node may only meet inside that node's box.
                        var meet = a.Intersection(b);
                        Assert.True(shared.Any(n => Inside(meet, l.Rooms[l.Nodes[n].Room].Interior)), $"pieces {i} and {j} overlap: {a} / {b}");
                    }
            }
    }

    private static bool Inside(Aabb3i inner, Aabb3i outer) => inner.Intersection(outer).Equals(inner);

    [Fact]
    public void PlansOnARealTinyWorld()
    {
        // The site as the dungeon pass picks it: snow side, 0.86 of the land radius, stepping inward onto solid land.
        var gen = new WorldGenerator(TestPaths.Content, new WorldSettings { Seed = 1234, Size = WorldSizePreset.Tiny });
        var plan = gen.Plan;
        float cx = plan.Size.X * 0.5f, cz = plan.Size.Z * 0.5f, dist = MathF.Min(plan.Size.X, plan.Size.Z) * 0.42f * 0.86f;
        float x = cx + Trig.Cos(plan.SnowAngle) * dist, z = cz + Trig.Sin(plan.SnowAngle) * dist;
        for (int i = 0; i < 20 && plan.Landness.Sample(x, z) < 0.97f; i++)
        {
            dist *= 0.95f;
            x = cx + Trig.Cos(plan.SnowAngle) * dist;
            z = cz + Trig.Sin(plan.SnowAngle) * dist;
        }
        var inward = MathF.Abs(cx - x) >= MathF.Abs(cz - z) ? new Int3(Math.Sign(cx - x), 0, 0) : new Int3(0, 0, Math.Sign(cz - z));
        Func<int, int, int> surface = (sx, sz) => (int)MathF.Floor(gen.Terrain.SurfaceHeight(sx, sz));
        var site = new DungeonSite
        {
            Gate = new Int3((int)x, surface((int)x, (int)z), (int)z), Inward = inward, WorldSize = plan.Size, Bands = plan.Bands,
            Surface = surface, Landness = (sx, sz) => plan.Landness.Sample(sx, sz),
        };
        var budget = DungeonBudget.For(WorldSizePreset.Tiny);
        var l = DungeonPlanner.Plan(site, budget, plan.Seeds.Child("dungeon"));
        Assert.True(l.Rooms.Count(r => r.Kind is not (RoomKind.Keep or RoomKind.Junction or RoomKind.Alcove)) >= budget.MinRooms);
        Assert.True(l.ChestSpots.Count >= budget.LockedChests);
        Assert.True(l.TrunkCells >= budget.HallCells / 2, $"trunk {l.TrunkCells}");
        Assert.Equal(l.Hash(), DungeonPlanner.Plan(site, budget, plan.Seeds.Child("dungeon")).Hash());
    }

    [Fact]
    public void DegradesGracefullyOnACrampedSite()
    {
        // Ground barely above the floor limit: nothing but the keep and its shaft fits, and planning must not fail.
        var good = SyntheticSite(3, WorldSizePreset.Tiny);
        int low = DungeonBudget.For(WorldSizePreset.Tiny).FloorY(good.Bands) + 24;
        var site = new DungeonSite
        {
            Gate = new Int3(good.Gate.X, low, good.Gate.Z), Inward = good.Inward, WorldSize = good.WorldSize, Bands = good.Bands,
            Surface = (_, _) => low, Landness = good.Landness,
        };
        var l = DungeonPlanner.Plan(site, DungeonBudget.For(WorldSizePreset.Tiny), SeedTree.FromWorldSeed(3));
        Assert.Equal(HallKind.Shaft, l.Halls[0].Kind);
        Assert.True(l.Halls[0].End.Y >= l.FloorLimit);
        Assert.Single(l.ChestSpots.Where(c => c.ShadowKey).DefaultIfEmpty(new DungeonChestSpot(default, 0, default, true)));
    }

    [Fact]
    public void LargePlansQuickly()
    {
        var site = SyntheticSite(7, WorldSizePreset.Large);
        var budget = DungeonBudget.For(WorldSizePreset.Large);
        DungeonPlanner.Plan(site, budget, SeedTree.FromWorldSeed(7)); // warm-up (JIT)
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var l = DungeonPlanner.Plan(site, budget, SeedTree.FromWorldSeed(8));
        sw.Stop();
        Assert.True(sw.ElapsedMilliseconds < 600, $"Large plan took {sw.ElapsedMilliseconds} ms");
        Assert.True(l.TrunkCells >= budget.HallCells * 3 / 4, $"trunk {l.TrunkCells}");
        Assert.True(l.Rooms.Count(r => r.Kind is not (RoomKind.Keep or RoomKind.Junction or RoomKind.Alcove)) >= budget.MinRooms);
    }
}
