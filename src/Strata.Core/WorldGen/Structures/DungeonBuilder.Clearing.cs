using Strata.Core.Common;

namespace Strata.Core.WorldGen.Structures;

public static partial class DungeonBuilder
{
    private const int ClearingMargin = 3, ClearingHeight = 24;

    /// <summary>
    /// Phase A, before the shell: where the ground round the keep rises above its floor (a hillside the flattest site
    /// still touches), a terraced cut clears it, outside the zone (NoZone): each layer over the floor reaches one cell
    /// further than the one below it, so the walls stand free in a bowl with a 1:1 bank instead of being buried. On level
    /// ground no layer has ground in it and nothing is added.
    /// </summary>
    private static void Clearing(Ctx b, Aabb3i outer)
    {
        var f = Footprint(outer);
        int floor = b.Layout.KeepInterior.Min.Y, reach = ClearingMargin + ClearingHeight;
        int x0 = f.Min.X - reach, z0 = f.Min.Z - reach, w = f.Size.X + 2 * reach, d = f.Size.Z + 2 * reach;
        var heights = new int[w * d];
        for (int z = 0; z < d; z++)
            for (int x = 0; x < w; x++) heights[z * w + x] = b.Surface(x0 + x, z0 + z);
        for (int h = 0; h < ClearingHeight; h++)
        {
            int grow = ClearingMargin + h, y = floor + h;
            bool ground = false;
            for (int z = reach - grow; z < d - reach + grow && !ground; z++)
                for (int x = reach - grow; x < w - reach + grow; x++)
                    if (heights[z * w + x] >= y) { ground = true; break; }
            if (!ground) continue;
            b.Bp.Carve(new Aabb3i(new Int3(f.Min.X - grow, y, f.Min.Z - grow), new Int3(f.Max.X + grow, y + 1, f.Max.Z + grow)), noZone: true);
        }
    }
}
