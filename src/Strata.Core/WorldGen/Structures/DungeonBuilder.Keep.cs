using Strata.Core.Common;

namespace Strata.Core.WorldGen.Structures;

public static partial class DungeonBuilder
{
    private const int KeepWall = 3, KeepRoof = 2, FacadeExtra = 2, Front = KeepWall + FacadeExtra;
    private const int TurretHeight = 24, PlinthDepth = 6, FoundationCap = 24, Apron = 3;
    private const int DoorWidth = 6, DoorHeight = 9, ApproachWidth = 10, ApproachHeight = 12, ApproachDepth = 12;
    private const int StepRows = 24, Landing = Apron + 3, GateRow = Apron + 1;

    /// <summary>
    /// Phase A for the keep, Terraria's tall brick entrance building: a foundation down to the ground (it never
    /// floats), walls 3 thick (a 2-cell brick shell and an inner panel) with a front facade 2 cells thicker, a 2-cell
    /// roof with a crenellated parapet, and four corner turrets with stepped caps.
    /// </summary>
    private static void KeepShell(Ctx b)
    {
        var k = b.Layout.KeepInterior;
        int floor = k.Min.Y;
        var outer = Outer(b);
        Clearing(b, outer);
        Foundation(b, outer);
        b.Bp.Fill(new Aabb3i(new Int3(outer.Min.X, floor - KeepRoof, outer.Min.Z), new Int3(outer.Max.X, k.Max.Y + KeepRoof, outer.Max.Z)), b.Brick);
        Panels(b, k, b.Style.Panel(b.Layout.Rooms[0].PanelStyle), true, true);
        Parapet(b, outer, k.Max.Y + KeepRoof);
        int top = floor + TurretHeight;
        foreach (var (cx, cz) in Corners(outer))
        {
            b.Bp.Cylinder(cx, cz, 3f, Base(b, outer), top, b.Brick);
            for (int s = 0; s < 4; s++) b.Bp.Cylinder(cx, cz, 4f - s, top + s, top + s + 1, b.Brick); // stepped cap
        }
    }

    /// <summary>Phase C for the keep: the hall, the arched door facing inland, two slit windows beside it, and the
    /// approach in front (a NoZone cut and steps, so the keeper waits outside the zone).</summary>
    private static void KeepCarve(Ctx b)
    {
        b.Bp.Carve(b.Layout.KeepInterior);
        b.Bp.Carve(Door(b.Layout, 0, Front, 0, DoorWidth, 0, DoorHeight));
        b.Bp.Carve(Door(b.Layout, 0, Front, 0, DoorWidth - 2, DoorHeight, DoorHeight + 1)); // stepped arch
        b.Bp.Carve(Door(b.Layout, 0, Front, 0, DoorWidth - 4, DoorHeight + 1, DoorHeight + 2));
        foreach (int side in new[] { -6, 6 }) b.Bp.Carve(Door(b.Layout, 0, Front, side, 1, 4, 9));
        Approach(b);
    }

    /// <summary>Phase D for the keep: a pillared foyer around the opening of the entrance shaft (hall 0, whose carve
    /// cuts the 10 x 10 hole in the floor; its platforms lead down to the trunk).</summary>
    private static void KeepFixtures(Ctx b)
    {
        var k = b.Layout.KeepInterior;
        foreach (int x in new[] { k.Min.X + 2, k.Max.X - 4 })
            foreach (int z in new[] { k.Min.Z + 2, k.Max.Z - 4 })
                b.Bp.Fill(new Aabb3i(new Int3(x, k.Min.Y, z), new Int3(x + 2, k.Max.Y, z + 2)), b.Brick);
    }

    /// <summary>The keep's outer walls (interior grown by the wall thickness, the front by the facade too).</summary>
    private static Aabb3i Outer(Ctx b)
    {
        var k = b.Layout.KeepInterior;
        var n = b.Layout.Inward;
        var lo = new Int3(k.Min.X - KeepWall - (n.X < 0 ? FacadeExtra : 0), k.Min.Y, k.Min.Z - KeepWall - (n.Z < 0 ? FacadeExtra : 0));
        var hi = new Int3(k.Max.X + KeepWall + (n.X > 0 ? FacadeExtra : 0), k.Max.Y, k.Max.Z + KeepWall + (n.Z > 0 ? FacadeExtra : 0));
        return new Aabb3i(lo, hi);
    }

    private static (float X, float Z)[] Corners(Aabb3i o) =>
        new[] { (o.Min.X + 0.5f, o.Min.Z + 0.5f), (o.Max.X - 0.5f, o.Min.Z + 0.5f), (o.Min.X + 0.5f, o.Max.Z - 0.5f), (o.Max.X - 0.5f, o.Max.Z - 0.5f) };

    /// <summary>The footprint the foundation covers: the walls plus the turrets and an apron round them.</summary>
    private static Aabb3i Footprint(Aabb3i outer) =>
        new(new Int3(outer.Min.X - Apron, outer.Min.Y, outer.Min.Z - Apron), new Int3(outer.Max.X + Apron, outer.Max.Y, outer.Max.Z + Apron));

    /// <summary>The foundation's common base: 6 under the lowest ground of the footprint, but at most 24 under the
    /// floor (deeper ground is reached column by column instead).</summary>
    private static int Base(Ctx b, Aabb3i outer)
    {
        var f = Footprint(outer);
        int lowest = int.MaxValue;
        for (int x = f.Min.X; x < f.Max.X; x++)
            for (int z = f.Min.Z; z < f.Max.Z; z++) lowest = Math.Min(lowest, b.Surface(x, z));
        return Math.Max(lowest - PlinthDepth, b.Layout.KeepInterior.Min.Y - KeepRoof - FoundationCap);
    }

    /// <summary>Brick from below the ground up to the keep floor under the whole footprint, in 4 x 4 blocks that each
    /// reach 2 cells into their own lowest ground, so on a slope it steps down like a plinth and nothing hangs in air.</summary>
    private static void Foundation(Ctx b, Aabb3i outer)
    {
        var f = Footprint(outer);
        int floor = b.Layout.KeepInterior.Min.Y, common = Base(b, outer);
        for (int x0 = f.Min.X; x0 < f.Max.X; x0 += 4)
            for (int z0 = f.Min.Z; z0 < f.Max.Z; z0 += 4)
            {
                int x1 = Math.Min(x0 + 4, f.Max.X), z1 = Math.Min(z0 + 4, f.Max.Z), low = int.MaxValue;
                for (int x = x0; x < x1; x++)
                    for (int z = z0; z < z1; z++) low = Math.Min(low, b.Surface(x, z));
                b.Bp.Fill(new Aabb3i(new Int3(x0, Math.Min(common, low - 2), z0), new Int3(x1, floor, z1)), b.Brick);
            }
    }

    /// <summary>Merlons 2 long and 2 tall every 4 cells round the roof's edge.</summary>
    private static void Parapet(Ctx b, Aabb3i o, int y)
    {
        for (int x = o.Min.X; x < o.Max.X; x += 4)
        {
            int x1 = Math.Min(x + 2, o.Max.X);
            b.Bp.Fill(new Aabb3i(new Int3(x, y, o.Min.Z), new Int3(x1, y + 2, o.Min.Z + 1)), b.Brick);
            b.Bp.Fill(new Aabb3i(new Int3(x, y, o.Max.Z - 1), new Int3(x1, y + 2, o.Max.Z)), b.Brick);
        }
        for (int z = o.Min.Z + 4; z < o.Max.Z - 1; z += 4)
        {
            int z1 = Math.Min(z + 2, o.Max.Z - 1);
            b.Bp.Fill(new Aabb3i(new Int3(o.Min.X, y, z), new Int3(o.Min.X + 1, y + 2, z1)), b.Brick);
            b.Bp.Fill(new Aabb3i(new Int3(o.Max.X - 1, y, z), new Int3(o.Max.X, y + 2, z1)), b.Brick);
        }
    }

    /// <summary>
    /// The way up to the door, outside the zone (NoZone): rows outward from the facade, a landing level with the floor
    /// (the foundation's apron, then <see cref="Landing"/> - <see cref="Apron"/> rows of NoZone brick where the ground
    /// is lower), then each row at most one cell lower than the last until it meets the ground. Each of the first 12 rows is cut clear above its standing height (a 10 wide, 12 tall approach, level
    /// with the floor through higher ground); where the ground is lower than a row, brick steps fill up to it.
    /// </summary>
    private static void Approach(Ctx b)
    {
        int floor = b.Layout.KeepInterior.Min.Y;
        int[] feet = ApproachFeet(b.Layout, b.Surface, out int[] ground);
        for (int row = 0; row < feet.Length; row++)
        {
            var cut = Door(b.Layout, Front + row, Front + row + 1, 0, ApproachWidth, 0, 1);
            if (row < ApproachDepth) b.Bp.Carve(new Aabb3i(new Int3(cut.Min.X, feet[row], cut.Min.Z), new Int3(cut.Max.X, floor + ApproachHeight, cut.Max.Z)), noZone: true);
            if (row >= Apron && ground[row] + 1 < feet[row])
                b.Bp.Fill(new Aabb3i(new Int3(cut.Min.X, ground[row] - 1, cut.Min.Z), new Int3(cut.Max.X, feet[row], cut.Max.Z)), b.Brick, noZone: true);
        }
    }

    /// <summary>Standing heights (feet cells) of the approach rows and the lowest ground under each row; rows end
    /// once the steps meet the ground past the cut.</summary>
    private static int[] ApproachFeet(Dungeon.DungeonLayout layout, Func<int, int, int> surface, out int[] ground)
    {
        int floor = layout.KeepInterior.Min.Y;
        var feet = new List<int>();
        var lows = new List<int>();
        int previous = floor;
        for (int row = 0; row < StepRows; row++)
        {
            var cut = Door(layout, Front + row, Front + row + 1, 0, ApproachWidth, 0, 1);
            int low = int.MaxValue;
            for (int x = cut.Min.X; x < cut.Max.X; x++)
                for (int z = cut.Min.Z; z < cut.Max.Z; z++) low = Math.Min(low, surface(x, z));
            int at = row < Landing ? floor : Math.Max(previous - 1, Math.Min(low + 1, floor));
            if (row >= ApproachDepth && low + 1 >= at) break;
            feet.Add(at);
            lows.Add(low);
            previous = at;
        }
        ground = lows.ToArray();
        return feet.ToArray();
    }

    /// <summary>The <c>dungeon_gate</c> landmark: the standing cell on the landing just past the apron in front of the
    /// keep's door, outside the zone (where the Old Man waits).</summary>
    public static Int3 GateCell(Dungeon.DungeonLayout layout, Func<int, int, int> surface)
    {
        var c = layout.Door + layout.Inward * (Front + GateRow);
        return new Int3(c.X, ApproachFeet(layout, surface, out _)[GateRow], c.Z);
    }

    /// <summary>A box through or in front of the keep's front wall: from <paramref name="near"/> to
    /// <paramref name="far"/> cells outward of the door cell, <paramref name="width"/> across centred
    /// <paramref name="offset"/> cells off the door, from <paramref name="y0"/> to <paramref name="y1"/> over the floor.</summary>
    private static Aabb3i Door(Dungeon.DungeonLayout layout, int near, int far, int offset, int width, int y0, int y1)
    {
        var d = layout.Door;
        var n = layout.Inward;
        var across = new Int3(n.Z != 0 ? 1 : 0, 0, n.X != 0 ? 1 : 0);
        var p0 = d + n * near + across * (offset - width / 2);
        var p1 = d + n * (far - 1) + across * (offset - width / 2 + width - 1);
        return new Aabb3i(new Int3(Math.Min(p0.X, p1.X), d.Y + y0, Math.Min(p0.Z, p1.Z)), new Int3(Math.Max(p0.X, p1.X) + 1, d.Y + y1, Math.Max(p0.Z, p1.Z) + 1));
    }
}
