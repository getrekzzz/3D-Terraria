using Strata.Core.Common;
using Strata.Core.Content;
using Strata.Core.Determinism;

namespace Strata.Core.WorldGen.Structures;

/// <summary>
/// The Jungle Temple: a squat Lihzahrd Brick block buried in the deep jungle (only a Picksaw cuts its walls). A locked
/// door on one face opens with the Temple Key; inside, nested walls with staggered gaps wind to the central sanctum
/// and the Lihzahrd Altar, flanked by chests.
/// </summary>
public static class TempleBuilder
{
    public const string Brick = "lihzahrd_brick";

    public static Blueprint Build(ContentDatabase content, Int3 center, ref Pcg32 rng)
    {
        var bp = new Blueprint();
        int half = rng.Range(30, 36), height = 22;
        int floor = center.Y - height / 2;
        var outer = new Aabb3i(new Int3(center.X - half, floor - 2, center.Z - half), new Int3(center.X + half, floor + height, center.Z + half));
        bp.Fill(outer, Brick);
        bp.Carve(new Aabb3i(outer.Min + new Int3(2, 2, 2), outer.Max - new Int3(2, 2, 2)));

        // Nested square walls, each with one gap on a different side: a winding path to the sanctum.
        int[] rings = { half - 10, half - 19 };
        for (int r = 0; r < rings.Length; r++)
        {
            int ring = rings[r];
            var box = new Aabb3i(new Int3(center.X - ring, floor, center.Z - ring), new Int3(center.X + ring, floor + height - 4, center.Z + ring));
            bp.Fill(box, Brick);
            bp.Carve(new Aabb3i(box.Min + new Int3(2, 0, 2), box.Max - new Int3(2, 0, 2)));
            // Gap: +X side for the outer ring, −Z side for the inner one (never lined up with the door on −X).
            var gap = r == 0
                ? new Aabb3i(new Int3(box.Max.X - 2, floor, center.Z - 2), new Int3(box.Max.X, floor + 6, center.Z + 2))
                : new Aabb3i(new Int3(center.X - 2, floor, box.Min.Z), new Int3(center.X + 2, floor + 6, box.Min.Z + 2));
            bp.Carve(gap);
        }
        // Pillars in the outer corridor.
        for (int i = -1; i <= 1; i += 2)
            for (int j = -1; j <= 1; j += 2)
            {
                int px = center.X + i * (half - 6), pz = center.Z + j * (half - 6);
                bp.Fill(new Aabb3i(new Int3(px - 1, floor, pz - 1), new Int3(px + 1, floor + height - 4, pz + 1)), Brick);
            }

        // The door: a passage through the −X wall holding the locked Lihzahrd Door.
        var tiles = content.TileObjects;
        bp.Carve(new Aabb3i(new Int3(outer.Min.X - 4, floor, center.Z - 1), new Int3(outer.Min.X + 2, floor + 4, center.Z + 1)));
        bp.Object(tiles.Get("lihzahrd_door"), new Int3(outer.Min.X + 1, floor, center.Z - 1), 1);

        // The sanctum: the altar, two chests and lamps.
        bp.Object(tiles.Get("lihzahrd_altar"), new Int3(center.X - 2, floor, center.Z - 1), 0);
        bp.Object(tiles.Get("gold_chest"), new Int3(center.X - 6, floor, center.Z + 3), 0, 0, "temple_chest");
        bp.Object(tiles.Get("gold_chest"), new Int3(center.X + 4, floor, center.Z + 3), 0, 0, "temple_chest");
        foreach (var (dx, dz) in new[] { (-6, -5), (5, -5), (-6, 6), (5, 6) })
            bp.Object(tiles.Get("torch"), new Int3(center.X + dx, floor, center.Z + dz), 0);
        // Dim torches down the corridors: the outer ring's corners and side midpoints, the middle ring's corners.
        int o = half - 4, m = half - 15;
        foreach (var (dx, dz) in new[] { (-o, -o), (o, -o), (-o, o), (o, o), (0, -o), (0, o), (o, 8), (o, -8), (-o, 5), (-o, -5),
                     (-m, -m), (m, -m), (-m, m), (m, m) })
            bp.Object(tiles.Get("torch"), new Int3(center.X + dx, floor, center.Z + dz), 0);
        // Treasure along the corridors.
        for (int i = 0; i < 4; i++)
        {
            int side = i % 2 == 0 ? 1 : -1;
            bp.Object(tiles.Get("pot"), new Int3(center.X + side * (half - 5), floor, center.Z + (i - 2) * 6), 0, i % 3);
        }
        return bp;
    }
}
