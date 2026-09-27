using System.Numerics;
using Strata.Core.Common;
using Strata.Core.Content;
using Strata.Core.Determinism;

namespace Strata.Core.WorldGen.Structures;

/// <summary>
/// Abandoned underground cabins: timber rooms with broken walls, cobwebs, furniture, a lit torch and a loot chest (a
/// Gold Chest in the cavern layer). Built on a cave tunnel, the tunnel runs in through doorways at both ends so the
/// cabin is found by exploring rather than only by digging. Some are two rooms: a living room and a bedroom or
/// workshop behind a partition, with a second chest.
/// </summary>
public static class CabinBuilder
{
    /// <param name="deep">Cavern layer: the main chest is a Gold Chest with gold-chest loot.</param>
    /// <param name="tunnel">Direction of the cave tunnel the cabin stands in (doorways face along it), or null.</param>
    public static Blueprint Build(ContentDatabase content, Int3 floorCenter, string wood, ref Pcg32 rng, bool deep = false, Vector3? tunnel = null)
    {
        var bp = new Blueprint { Flags = Voxels.CellFlags.None };
        bool twoRooms = rng.Chance(0.45f);
        int w = twoRooms ? rng.Range(20, 25) : rng.Range(12, 17), d = rng.Range(10, 14), h = rng.Range(8, 10);
        var min = new Int3(floorCenter.X - w / 2, floorCenter.Y - 1, floorCenter.Z - d / 2);
        var box = new Aabb3i(min, min + new Int3(w, h, d));
        var max = box.Max;
        int floorY = min.Y + 1;
        bp.Room(box, wood);
        int split = min.X + w / 2;
        if (twoRooms)
        {
            // A partition with a doorway between the rooms.
            bp.Fill(new Aabb3i(new Int3(split, floorY, min.Z + 1), new Int3(split + 1, max.Y - 1, max.Z - 1)), wood);
            int doorZ = min.Z + rng.Range(3, d - 5);
            bp.Carve(new Aabb3i(new Int3(split, floorY, doorZ), new Int3(split + 1, floorY + 5, doorZ + 3)));
        }

        // Decay: knock holes in walls and roof.
        for (int i = rng.Range(2, 5); i > 0; i--)
        {
            int side = rng.NextInt(4);
            int along = rng.Range(2, (side < 2 ? w : d) - 4);
            int y = rng.Range(2, h - 3);
            var hole = side switch
            {
                0 => new Aabb3i(new Int3(min.X + along, min.Y + y, min.Z), new Int3(min.X + along + 2, min.Y + y + 2, min.Z + 1)),
                1 => new Aabb3i(new Int3(min.X + along, min.Y + y, max.Z - 1), new Int3(min.X + along + 2, min.Y + y + 2, max.Z)),
                2 => new Aabb3i(new Int3(min.X, min.Y + y, min.Z + along), new Int3(min.X + 1, min.Y + y + 2, min.Z + along + 2)),
                _ => new Aabb3i(new Int3(max.X - 1, min.Y + y, min.Z + along), new Int3(max.X, min.Y + y + 2, min.Z + along + 2)),
            };
            bp.Carve(hole);
        }

        Entrances(bp, min, max, floorY, tunnel, ref rng);

        // Cobwebs in the upper corners.
        foreach (var corner in new[] { new Int3(min.X + 1, max.Y - 3, min.Z + 1), new Int3(max.X - 3, max.Y - 3, max.Z - 3), new Int3(min.X + 1, max.Y - 3, max.Z - 3) })
            if (rng.Chance(0.75f)) bp.Fill(new Aabb3i(corner, corner + new Int3(2, 2, 2)), "cobweb", onlyAir: true);

        var tiles = content.TileObjects;
        int roomEnd = twoRooms ? split : max.X - 1;
        // Living room: the chest in the far corner with a torch beside it, a table and chairs, pots along the walls.
        var chestAt = new Int3(roomEnd - 3, floorY, max.Z - 3);
        bp.Object(tiles.Get(deep ? "gold_chest" : "wooden_chest"), chestAt, 2, 0, deep ? "gold_chest" : "cabin_chest");
        bp.Object(tiles.Get("torch"), chestAt + new Int3(-2, 0, 1), 0);
        bp.Object(tiles.Get("wooden_table"), new Int3(min.X + 3, floorY, min.Z + 4), 0);
        bp.Object(tiles.Get("wooden_chair"), new Int3(min.X + 7, floorY, min.Z + 4), 1);
        if (rng.Chance(0.6f)) bp.Object(tiles.Get("wooden_chair"), new Int3(min.X + 1, floorY, min.Z + 4), 3);
        if (rng.Chance(0.7f)) bp.Object(tiles.Get("pot"), new Int3(roomEnd - 3, floorY, min.Z + 2), 0, rng.NextInt(3));
        if (rng.Chance(0.5f)) bp.Object(tiles.Get("pot"), new Int3(min.X + 1, floorY, max.Z - 3), 0, rng.NextInt(3));
        if (twoRooms) BackRoom(bp, content, split + 1, max.X - 1, min.Z, max.Z, floorY, ref rng);
        return bp;
    }

    /// <summary>The second room: a bedroom or a workshop, a second (plain) chest, a torch and a pot.</summary>
    private static void BackRoom(Blueprint bp, ContentDatabase content, int x0, int x1, int z0, int z1, int floorY, ref Pcg32 rng)
    {
        var tiles = content.TileObjects;
        if (rng.Chance(0.5f))
        {
            bp.Object(tiles.Get("bed"), new Int3(x1 - 4, floorY, z0 + 2), 0);
            bp.Object(tiles.Get("wooden_chair"), new Int3(x0 + 1, floorY, z1 - 4), 1);
        }
        else
        {
            bp.Object(tiles.Get("work_bench"), new Int3(x0 + 1, floorY, z0 + 2), 0);
            bp.Object(tiles.Get(rng.Chance(0.5f) ? "iron_anvil" : "lead_anvil"), new Int3(x1 - 3, floorY, z0 + 2), 0);
            if (rng.Chance(0.5f)) bp.Object(tiles.Get("placed_bottle"), new Int3(x0 + 2, floorY, z0 + 5), 0);
        }
        if (rng.Chance(0.6f)) bp.Object(tiles.Get("wooden_chest"), new Int3(x1 - 3, floorY, z1 - 3), 2, 0, "surface_chest");
        bp.Object(tiles.Get("torch"), new Int3(x0 + 1, floorY, z1 - 2), 0);
        if (rng.Chance(0.6f)) bp.Object(tiles.Get("pot"), new Int3(x0 + 1, floorY, z1 - 5), 0, rng.NextInt(3));
    }

    /// <summary>
    /// Doorways (and a short passage out through the rock) at both ends along the tunnel, so the tunnel flows through
    /// the cabin; without a tunnel, one doorway on the -Z wall as before.
    /// </summary>
    private static void Entrances(Blueprint bp, Int3 min, Int3 max, int floorY, Vector3? tunnel, ref Pcg32 rng)
    {
        const int reach = 7;
        if (tunnel is not { } dir)
        {
            int doorX = min.X + rng.Range(2, max.X - min.X - 4);
            bp.Carve(new Aabb3i(new Int3(doorX, floorY, min.Z), new Int3(doorX + 2, floorY + 4, min.Z + 1)));
            return;
        }
        if (MathF.Abs(dir.X) >= MathF.Abs(dir.Z))
        {
            int z = (min.Z + max.Z) / 2 - 1;
            bp.Carve(new Aabb3i(new Int3(min.X - reach, floorY, z), new Int3(min.X + 1, floorY + 4, z + 3)));
            bp.Carve(new Aabb3i(new Int3(max.X - 1, floorY, z), new Int3(max.X + reach, floorY + 4, z + 3)));
        }
        else
        {
            int x = (min.X + max.X) / 2 - 1;
            bp.Carve(new Aabb3i(new Int3(x, floorY, min.Z - reach), new Int3(x + 3, floorY + 4, min.Z + 1)));
            bp.Carve(new Aabb3i(new Int3(x, floorY, max.Z - 1), new Int3(x + 3, floorY + 4, max.Z + reach)));
        }
    }
}
