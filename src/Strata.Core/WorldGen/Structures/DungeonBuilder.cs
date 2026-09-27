using Strata.Core.Common;
using Strata.Core.Content;
using Strata.Core.Determinism;
using Strata.Core.Voxels;

namespace Strata.Core.WorldGen.Structures;

/// <summary>
/// The Old Crypt: a brick gatehouse on the surface, a great descent shaft, then a multi-level
/// graph of halls joined by corridors and vertical wells. Walls are protected crypt brick.
/// </summary>
public static class DungeonBuilder
{
    private const int SlotX = 24, SlotY = 16, SlotZ = 24;

    public static Blueprint Build(ContentDatabase content, Int3 gate, ref Pcg32 rng, int nx, int ny, int nz)
    {
        var bp = new Blueprint { Flags = CellFlags.Structure };
        string brick = rng.Pick(new[] { "blue_brick", "green_brick", "pink_brick" });
        byte platform = ShapeCode.Pack(CellShape.Platform, 0);

        // Gatehouse with crenellations and an open doorway.
        var tower = new Aabb3i(new Int3(gate.X - 9, gate.Y - 3, gate.Z - 9), new Int3(gate.X + 9, gate.Y + 24, gate.Z + 9));
        bp.Room(tower, brick);
        for (int i = 0; i < 18; i += 4)
        {
            bp.Fill(new Aabb3i(new Int3(tower.Min.X + i, tower.Max.Y, tower.Min.Z), new Int3(tower.Min.X + i + 2, tower.Max.Y + 2, tower.Min.Z + 1)), brick);
            bp.Fill(new Aabb3i(new Int3(tower.Min.X + i, tower.Max.Y, tower.Max.Z - 1), new Int3(tower.Min.X + i + 2, tower.Max.Y + 2, tower.Max.Z)), brick);
        }
        bp.Carve(new Aabb3i(new Int3(gate.X - 2, gate.Y - 2, tower.Min.Z), new Int3(gate.X + 2, gate.Y + 5, tower.Min.Z + 1)));

        // Descent shaft.
        int depth = rng.Range(46, 70);
        var shaft = new Aabb3i(new Int3(gate.X - 5, gate.Y - depth, gate.Z - 5), new Int3(gate.X + 5, gate.Y - 1, gate.Z + 5));
        bp.Room(shaft, brick);
        bp.Carve(new Aabb3i(new Int3(gate.X - 4, gate.Y - 3, gate.Z - 4), new Int3(gate.X + 4, gate.Y, gate.Z + 4)));
        for (int y = shaft.Min.Y + 8; y < shaft.Max.Y - 4; y += 10)
            bp.Fill(new Aabb3i(new Int3(gate.X - 4, y, gate.Z - 4), new Int3(gate.X + 4, y + 1, gate.Z + 4)), brick, platform);

        // Room graph below the shaft.
        var origin = new Int3(gate.X - SlotX / 2, shaft.Min.Y - ny * SlotY + 2, gate.Z - SlotZ / 2);
        var rooms = new Aabb3i[nx, ny, nz];
        var visited = new bool[nx, ny, nz];
        var stack = new Stack<Int3>();
        var entry = new Int3(0, ny - 1, 0);
        stack.Push(entry);
        visited[0, ny - 1, 0] = true;
        rooms[0, ny - 1, 0] = PlaceRoom(bp, origin, entry, brick, ref rng);
        bp.Carve(new Aabb3i(new Int3(gate.X - 4, rooms[0, ny - 1, 0].Max.Y - 2, gate.Z - 4), new Int3(gate.X + 4, shaft.Min.Y + 1, gate.Z + 4)));

        Int3[] dirs = { new(1, 0, 0), new(-1, 0, 0), new(0, 0, 1), new(0, 0, -1), new(0, -1, 0), new(0, 1, 0) };
        while (stack.Count > 0)
        {
            var cur = stack.Peek();
            var options = dirs.Select(d => cur + d)
                .Where(n => n.X >= 0 && n.Y >= 0 && n.Z >= 0 && n.X < nx && n.Y < ny && n.Z < nz && !visited[n.X, n.Y, n.Z]).ToList();
            if (options.Count == 0 || rng.Chance(0.08f))
            {
                stack.Pop();
                continue;
            }
            var next = options[rng.NextInt(options.Count)];
            visited[next.X, next.Y, next.Z] = true;
            rooms[next.X, next.Y, next.Z] = PlaceRoom(bp, origin, next, brick, ref rng);
            Connect(bp, rooms[cur.X, cur.Y, cur.Z], rooms[next.X, next.Y, next.Z], next - cur, brick, platform);
            stack.Push(next);
        }

        for (int y = 0; y < ny; y++)
            for (int z = 0; z < nz; z++)
                for (int x = 0; x < nx; x++)
                    if (visited[x, y, z]) Furnish(bp, content, rooms[x, y, z], ref rng);
        return bp;
    }

    private static Aabb3i PlaceRoom(Blueprint bp, Int3 origin, Int3 slot, string brick, ref Pcg32 rng)
    {
        int w = rng.Range(12, 19), h = rng.Range(9, 12), d = rng.Range(12, 19);
        var slotMin = origin + new Int3(slot.X * SlotX, slot.Y * SlotY, slot.Z * SlotZ);
        var min = slotMin + new Int3((SlotX - w) / 2, 1, (SlotZ - d) / 2);
        var room = new Aabb3i(min, min + new Int3(w, h, d));
        bp.Room(room, brick);
        return room;
    }

    private static void Connect(Blueprint bp, Aabb3i a, Aabb3i b, Int3 dir, string brick, byte platform)
    {
        var ca = Center(a);
        var cb = Center(b);
        if (dir.Y != 0)
        {
            var upper = dir.Y > 0 ? b : a;
            var lower = dir.Y > 0 ? a : b;
            var c = Center(lower);
            var well = new Aabb3i(new Int3(c.X - 3, lower.Max.Y - 2, c.Z - 3), new Int3(c.X + 3, upper.Min.Y + 2, c.Z + 3));
            bp.Fill(well.Expand(1), brick, onlyAir: false);
            bp.Carve(well);
            for (int y = well.Min.Y + 5; y < well.Max.Y - 2; y += 6)
                bp.Fill(new Aabb3i(new Int3(well.Min.X, y, well.Min.Z), new Int3(well.Max.X, y + 1, well.Max.Z)), brick, platform);
            return;
        }
        int floor = Math.Max(a.Min.Y, b.Min.Y) + 1;
        var lo = new Int3(Math.Min(ca.X, cb.X) - 2, floor, Math.Min(ca.Z, cb.Z) - 2);
        var hi = new Int3(Math.Max(ca.X, cb.X) + 3, floor + 6, Math.Max(ca.Z, cb.Z) + 3);
        var corridor = new Aabb3i(lo, hi);
        bp.Fill(corridor.Expand(1), brick);
        bp.Carve(corridor);
        bp.Carve(a.Expand(-1));
        bp.Carve(b.Expand(-1));
    }

    private static void Furnish(Blueprint bp, ContentDatabase content, Aabb3i room, ref Pcg32 rng)
    {
        var floor = new Int3(room.Min.X + 2, room.Min.Y + 1, room.Min.Z + 2);
        var tiles = content.TileObjects;
        if (rng.Chance(0.3f)) bp.Object(tiles.Get("gold_chest"), floor + new Int3(rng.Range(0, 4), 0, rng.Range(0, 4)), 0, 0, "cabin_chest");
        if (rng.Chance(0.7f)) bp.Object(tiles.Get("candle"), new Int3(room.Max.X - 3, room.Min.Y + 1, room.Max.Z - 3), 0);
        for (int i = rng.Range(0, 4); i > 0; i--)
            bp.Object(tiles.Get("pot"), new Int3(room.Min.X + 2 + i * 3, room.Min.Y + 1, room.Max.Z - 4), 0, i % 3);
        if (rng.Chance(0.5f))
        {
            var top = new Int3(room.Min.X + 1, room.Max.Y - 3, room.Min.Z + 1);
            bp.Fill(new Aabb3i(top, top + new Int3(3, 2, 3)), "cobweb", onlyAir: true);
        }
    }

    private static Int3 Center(Aabb3i b) => new((b.Min.X + b.Max.X) / 2, b.Min.Y, (b.Min.Z + b.Max.Z) / 2);
}
