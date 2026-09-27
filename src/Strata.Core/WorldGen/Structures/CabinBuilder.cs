using Strata.Core.Common;
using Strata.Core.Content;
using Strata.Core.Determinism;

namespace Strata.Core.WorldGen.Structures;

/// <summary>Abandoned underground cabins: timber rooms with broken walls, cobwebs, furniture and a loot chest.</summary>
public static class CabinBuilder
{
    public static Blueprint Build(ContentDatabase content, Int3 floorCenter, string wood, ref Pcg32 rng)
    {
        var bp = new Blueprint { Flags = Voxels.CellFlags.None };
        int w = rng.Range(12, 17), d = rng.Range(10, 14), h = rng.Range(8, 10);
        var min = new Int3(floorCenter.X - w / 2, floorCenter.Y - 1, floorCenter.Z - d / 2);
        var box = new Aabb3i(min, min + new Int3(w, h, d));
        bp.Room(box, wood);

        // Decay: knock holes in walls and roof.
        for (int i = rng.Range(2, 5); i > 0; i--)
        {
            int side = rng.NextInt(4);
            int along = rng.Range(2, (side < 2 ? w : d) - 4);
            int y = rng.Range(2, h - 3);
            var hole = side switch
            {
                0 => new Aabb3i(new Int3(min.X + along, min.Y + y, min.Z), new Int3(min.X + along + 2, min.Y + y + 2, min.Z + 1)),
                1 => new Aabb3i(new Int3(min.X + along, min.Y + y, box.Max.Z - 1), new Int3(min.X + along + 2, min.Y + y + 2, box.Max.Z)),
                2 => new Aabb3i(new Int3(min.X, min.Y + y, min.Z + along), new Int3(min.X + 1, min.Y + y + 2, min.Z + along + 2)),
                _ => new Aabb3i(new Int3(box.Max.X - 1, min.Y + y, min.Z + along), new Int3(box.Max.X, min.Y + y + 2, min.Z + along + 2)),
            };
            bp.Carve(hole);
        }

        // Doorway on the -Z wall.
        int doorX = min.X + rng.Range(2, w - 4);
        bp.Carve(new Aabb3i(new Int3(doorX, min.Y + 1, min.Z), new Int3(doorX + 2, min.Y + 5, min.Z + 1)));

        // Cobwebs in the upper corners.
        foreach (var corner in new[] { new Int3(min.X + 1, box.Max.Y - 3, min.Z + 1), new Int3(box.Max.X - 3, box.Max.Y - 3, box.Max.Z - 3) })
            bp.Fill(new Aabb3i(corner, corner + new Int3(2, 2, 2)), "cobweb", onlyAir: true);

        var floorY = min.Y + 1;
        var tiles = content.TileObjects;
        bp.Object(tiles.Get("wooden_chest"), new Int3(box.Max.X - 4, floorY, box.Max.Z - 4), 0, 0, "cabin_chest");
        bp.Object(tiles.Get("wooden_table"), new Int3(min.X + 2, floorY, min.Z + 3), 0);
        bp.Object(tiles.Get("wooden_chair"), new Int3(min.X + 6, floorY, min.Z + 4), 1);
        bp.Object(tiles.Get("torch"), new Int3(min.X + 1, floorY, box.Max.Z - 2), 0);
        if (rng.Chance(0.6f)) bp.Object(tiles.Get("pot"), new Int3(box.Max.X - 4, floorY, min.Z + 2), 0, rng.NextInt(3));
        return bp;
    }
}
