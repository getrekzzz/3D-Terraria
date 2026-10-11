using System.Numerics;
using Strata.Core.Common;
using Strata.Core.Content.Defs;
using Strata.Core.Tests.Support;
using Strata.Core.World;
using Strata.Core.WorldGen;
using Strata.Core.WorldGen.Caves;

namespace Strata.Core.Tests.WorldGen;

/// <summary>
/// The cave network: themed rooms on four levels joined by walkable passages, switchback descents between the levels,
/// walk-in entrances from the surface, and planned treasure that ends up standing on the rooms' floors.
/// </summary>
public class CaveNetworkTests
{
    private static readonly Lazy<WorldGenerator> Small = new(() => new WorldGenerator(TestPaths.Content, new WorldSettings { Seed = 1234, Size = WorldSizePreset.Small }));
    private static readonly Lazy<WorldGenerator> Tiny = new(() => new WorldGenerator(TestPaths.Content, new WorldSettings { Seed = 1234, Size = WorldSizePreset.Tiny }));

    private static readonly CaveRoomKind[] Minor = { CaveRoomKind.Landing, CaveRoomKind.Niche, CaveRoomKind.Alcove };

    private static List<CaveRoomFeature> Rooms(WorldGenerator gen) => gen.Plan.Features.All.OfType<CaveRoomFeature>().ToList();

    private static List<CavePassageFeature> Passages(WorldGenerator gen) => gen.Plan.Features.All.OfType<CavePassageFeature>().ToList();

    [Fact]
    public void EveryLevelHasItsOwnKindsOfRoom()
    {
        var rooms = Rooms(Small.Value).Where(r => !Minor.Contains(r.Room)).ToList();
        var expected = new[]
        {
            new[] { CaveRoomKind.Hall, CaveRoomKind.Gallery, CaveRoomKind.Grotto },
            new[] { CaveRoomKind.Dome, CaveRoomKind.Lake, CaveRoomKind.Terraces, CaveRoomKind.Grotto },
            new[] { CaveRoomKind.Pillars, CaveRoomKind.Crystal, CaveRoomKind.Dome, CaveRoomKind.Lake },
            new[] { CaveRoomKind.Magma, CaveRoomKind.Pillars },
        };
        var bands = Small.Value.Plan.Bands;
        for (int level = 0; level < CaveTuning.Levels; level++)
        {
            var onLevel = rooms.Where(r => r.Level == level).ToList();
            Assert.True(onLevel.Count >= 100, $"level {level}: only {onLevel.Count} rooms");
            Assert.Equal(expected[level].OrderBy(k => k), onLevel.Select(r => r.Room).Distinct().OrderBy(k => k));
            foreach (var r in onLevel)
            {
                float depth = bands.NormalizedDepth(r.FloorY);
                Assert.InRange(depth, CaveTuning.DepthMin[level] - 0.01f, CaveTuning.DepthMax[level] + 0.01f);
            }
        }
    }

    [Fact]
    public void PassagesAreWalkableAndMeetRoomsAtFloorLevel()
    {
        var gen = Small.Value;
        var rooms = Rooms(gen);
        var passages = Passages(gen);
        Assert.All(passages, p => Assert.True(p.Slope <= CaveTuning.MaxSlope + 1e-3f, $"passage {p.A}→{p.B} slope {p.Slope:F2}"));
        // Every door is on the room's floor row, and a passage starts there (or it opens into a side alcove).
        var ends = passages.SelectMany(p => new[] { p.A, p.B }).ToList();
        var alcoves = rooms.Where(r => r.Room == CaveRoomKind.Alcove).ToList();
        foreach (var room in rooms)
            foreach (var door in room.Doors)
            {
                Assert.Equal(room.FloorY, door.Y);
                bool passage = ends.Any(e => Vector3.DistanceSquared(e, door) < 0.01f);
                bool alcove = alcoves.Any(a => a.FloorY == room.FloorY && a.Footprint(door.X, door.Z) < 2f);
                Assert.True(passage || alcove, $"door {door} of a {room.Room} leads nowhere");
            }
    }

    [Fact]
    public void TheNetworkHangsTogether()
    {
        var gen = Small.Value;
        var rooms = Rooms(gen);
        var passages = Passages(gen);
        // Nodes: rooms, then passages. Passages join at shared ends; a passage joins a room when an end lies on its floor.
        int n = rooms.Count + passages.Count;
        var parent = Enumerable.Range(0, n).ToArray();
        int Find(int i) => parent[i] == i ? i : parent[i] = Find(parent[i]);
        void Union(int a, int b) => parent[Find(a)] = Find(b);
        var byEnd = new Dictionary<(int, int, int), int>();
        for (int k = 0; k < passages.Count; k++)
            foreach (var e in new[] { passages[k].A, passages[k].B })
            {
                var key = ((int)MathF.Round(e.X * 4), (int)MathF.Round(e.Y * 4), (int)MathF.Round(e.Z * 4));
                if (byEnd.TryGetValue(key, out int other)) Union(rooms.Count + k, other);
                else byEnd[key] = rooms.Count + k;
            }
        for (int r = 0; r < rooms.Count; r++)
        {
            var room = rooms[r];
            for (int k = 0; k < passages.Count; k++)
            {
                var p = passages[k];
                if (!p.Bounds.Intersects(room.Bounds)) continue;
                foreach (var e in new[] { p.A, p.B })
                    if (MathF.Abs(e.Y - room.FloorY) <= 1f && room.Footprint(e.X, e.Z) <= 1f) Union(r, rooms.Count + k);
            }
        }
        var main = Enumerable.Range(0, rooms.Count).Where(r => !Minor.Contains(rooms[r].Room)).ToList();
        var biggest = main.GroupBy(Find).OrderByDescending(g => g.Count()).First();
        Assert.True(biggest.Count() * 100 >= main.Count * 90, $"largest network piece holds {biggest.Count()} of {main.Count} rooms");
        for (int level = 0; level < CaveTuning.Levels; level++)
            Assert.Contains(biggest, r => rooms[r].Level == level);
        // Entrances from the surface lead into it.
        var entrances = passages.Where(p => p.A.Y >= gen.Terrain.SurfaceHeight(p.A.X, p.A.Z) - 3f).ToList();
        Assert.True(entrances.Count >= 15, $"only {entrances.Count} entrances");
        Assert.True(entrances.Count(p => Find(rooms.Count + passages.IndexOf(p)) == biggest.Key) * 2 >= entrances.Count);
    }

    [Fact]
    public void PlannedTreasureStandsOnTheFloor()
    {
        var gen = Tiny.Value;
        var chests = Rooms(gen).SelectMany(r => r.Treasure.Select(t => (Room: r, Treasure: t))).Where(x => x.Treasure.Loot != null).Take(12).ToList();
        Assert.True(chests.Count >= 6, $"only {chests.Count} planned chests in a tiny world");
        int found = 0;
        foreach (var (room, t) in chests)
        {
            var chunk = gen.Generate(ChunkMath.CellToChunk(t.Anchor));
            var obj = chunk.Objects.FirstOrDefault(o => o.Def.Id == t.Object && o.Anchor.X == t.Anchor.X && o.Anchor.Z == t.Anchor.Z);
            if (obj == null) continue;
            found++;
            Assert.Equal(t.Loot, obj.PendingLoot);
            Assert.Equal(TileInteraction.Chest, obj.Def.Interaction);
            Assert.InRange(obj.Anchor.Y, room.FloorY - 3, room.FloorY + 3);
        }
        Assert.True(found * 4 >= chests.Count * 3, $"{found} of {chests.Count} planned chests were placed");
    }

    [Fact]
    public void UpsideDownWorldsKeepTheirRoomsInTheRock()
    {
        var gen = new WorldGenerator(TestPaths.Content, new WorldSettings { Seed = 1234, Size = WorldSizePreset.Small, Secret = SecretSeed.DontDigUp });
        var rooms = Rooms(gen).Where(r => !Minor.Contains(r.Room)).ToList();
        Assert.True(rooms.Count >= 200, $"only {rooms.Count} rooms");
        foreach (var r in rooms)
            Assert.True(gen.Terrain.SurfaceDepthAt(r.X, r.FloorY + r.Height, r.Z) >= 8f, $"a {r.Room} at {r.Center} breaks out of the rock");
        // No walk-in entrances: the surface is at the bottom.
        Assert.DoesNotContain(Passages(gen), p => gen.Terrain.SurfaceDepthAt(p.A.X, p.A.Y, p.A.Z) < 3f);
    }

    [Fact]
    public void ChestsRankByDepth()
    {
        foreach (var room in Rooms(Small.Value))
            foreach (var t in room.Treasure.Where(t => t.Loot != null))
            {
                string expected = room.Level == CaveTuning.Levels - 1 ? "obsidian_chest" : room.Level >= 1 || room.Room == CaveRoomKind.Niche ? "gold_chest" : "cave_chest";
                Assert.Equal(expected, t.Loot);
            }
    }
}
