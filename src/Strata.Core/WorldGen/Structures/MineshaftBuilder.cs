using Strata.Core.Common;
using Strata.Core.Content;
using Strata.Core.Determinism;

namespace Strata.Core.WorldGen.Structures;

/// <summary>
/// Abandoned mine networks: timber-braced corridors with rail tracks (future minecart routes),
/// lanterns, junctions and descending shafts, the odd pot along the way and a miner's chest at the far end.
/// </summary>
public static class MineshaftBuilder
{
    private static readonly Int3[] Directions = { new(1, 0, 0), new(0, 0, 1), new(-1, 0, 0), new(0, 0, -1) };

    public static Blueprint Build(ContentDatabase content, Int3 start, ref Pcg32 rng, int segments)
    {
        var bp = new Blueprint { Flags = Voxels.CellFlags.None };
        var rail = content.TileObjects.Get("minecart_track");
        var torch = content.TileObjects.Get("torch");
        var pot = content.TileObjects.Get("pot");
        var chest = content.TileObjects.Get("wooden_chest");
        var pos = start;
        int dir = rng.NextInt(4);
        for (int s = 0; s < segments; s++)
        {
            if (rng.Chance(0.2f))
            {
                // Descending shaft with a timber landing.
                int drop = rng.Range(8, 16);
                bp.Carve(new Aabb3i(new Int3(pos.X - 2, pos.Y - drop, pos.Z - 2), new Int3(pos.X + 2, pos.Y + 5, pos.Z + 2)));
                bp.Fill(new Aabb3i(new Int3(pos.X - 2, pos.Y - drop - 1, pos.Z - 2), new Int3(pos.X + 2, pos.Y - drop, pos.Z + 2)), "wood");
                pos = new Int3(pos.X, pos.Y - drop, pos.Z);
            }

            int len = rng.Range(16, 40);
            var d = Directions[dir];
            var side = new Int3(d.Z, 0, d.X);
            for (int i = 0; i < len; i++)
            {
                var c = pos + d * i;
                var lo = Int3.Min(c - side * 2, c + side * 2);
                var hi = Int3.Max(c - side * 2, c + side * 2);
                bp.Carve(new Aabb3i(lo, hi + new Int3(1, 5, 1)));
                bp.Fill(new Aabb3i(lo - new Int3(0, 1, 0), hi + new Int3(1, 0, 1)), "wood", onlyAir: true);
                if (i % 6 == 0)
                {
                    foreach (int sgn in new[] { -2, 2 })
                    {
                        var post = c + side * sgn;
                        bp.Fill(new Aabb3i(post, post + new Int3(1, 5, 1)), "wood");
                    }
                    var beamA = c - side * 2 + new Int3(0, 4, 0);
                    var beamB = c + side * 2 + new Int3(0, 4, 0);
                    bp.Fill(new Aabb3i(Int3.Min(beamA, beamB), Int3.Max(beamA, beamB) + Int3.One), "wood");
                }
                bp.Object(rail, c, 0);
                if (i % 12 == 6) bp.Object(torch, c + side, 0);
                else if (i % 17 == 11 && rng.Chance(0.5f)) bp.Object(pot, c - side * 2, 0, rng.NextInt(3));
            }
            pos += d * len;
            if (s == segments - 1)
            {
                // The end of the line: whatever the miners left behind.
                bp.Object(chest, pos - d * 3 + side, 0, 0, "cabin_chest");
                bp.Object(torch, pos - d * 4 - side, 0);
            }
            dir = (dir + (rng.Chance(0.5f) ? 1 : 3)) % 4;
        }
        return bp;
    }
}
