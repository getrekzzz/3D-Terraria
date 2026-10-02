using Strata.Core.Common;

namespace Strata.Core.WorldGen.Structures;

public static partial class DungeonBuilder
{
    /// <summary>Phase A for the keep: a 2-cell brick shell lined with panels.</summary>
    private static void KeepShell(Ctx b)
    {
        var keep = b.Layout.KeepInterior;
        b.Bp.Fill(keep.Expand(2), b.Brick);
        Panels(b, keep, b.Style.Panel(0), true, true);
    }

    /// <summary>Phase C for the keep: its hall, the doorway facing inland and a cut through the ground in front of it
    /// (not part of the zone, so the keeper waits outside).</summary>
    private static void KeepCarve(Ctx b)
    {
        var keep = b.Layout.KeepInterior;
        b.Bp.Carve(keep);
        b.Bp.Carve(Door(b, 0, 2, 6, 9));
        b.Bp.Carve(Door(b, 2, 14, 10, 12), noZone: true);
    }

    /// <summary>Phase D for the keep (nothing yet).</summary>
    private static void KeepFixtures(Ctx b)
    {
    }

    /// <summary>A box in front of the keep door: from <paramref name="near"/> to <paramref name="far"/> cells outward of
    /// the door cell, <paramref name="width"/> across (centred on the door) and <paramref name="height"/> up from the
    /// keep floor.</summary>
    private static Aabb3i Door(Ctx b, int near, int far, int width, int height)
    {
        var d = b.Layout.Door;
        var n = b.Layout.Inward;
        var p0 = d + n * near;
        var p1 = d + n * (far - 1);
        int c0 = -(width / 2), c1 = c0 + width;
        var across = new Int3(n.Z != 0 ? 1 : 0, 0, n.X != 0 ? 1 : 0);
        var lo = new Int3(Math.Min(p0.X, p1.X), d.Y, Math.Min(p0.Z, p1.Z)) + across * c0;
        var hi = new Int3(Math.Max(p0.X, p1.X) + 1, d.Y + height, Math.Max(p0.Z, p1.Z) + 1) + across * (c1 - 1);
        return new Aabb3i(lo, hi);
    }
}
