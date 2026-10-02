using Strata.Core.Common;
using Strata.Core.Voxels;
using Strata.Core.WorldGen.Structures.Dungeon;

namespace Strata.Core.WorldGen.Structures;

public static partial class DungeonBuilder
{
    /// <summary>Phase A: a 2-cell brick shell around every interior box (one per stair step, so stairs get a stepped
    /// shell).</summary>
    private static void HallShell(Ctx b, DungeonHall hall)
    {
        for (int i = 0; i < hall.InteriorCount; i++) b.Bp.Fill(hall.Interior(i).Expand(2), b.Brick);
    }

    /// <summary>Phase B: panels on the side faces. Stair steps only line the two long walls (their front and back
    /// faces are the neighbouring steps).</summary>
    private static void HallPanels(Ctx b, DungeonHall hall)
    {
        string panel = b.Style.Panel(hall.PanelStyle);
        if (hall.Kind != HallKind.Stairs)
        {
            Panels(b, hall.Interior(0), panel, true, true);
            return;
        }
        bool alongX = hall.Dir.X != 0;
        for (int i = 0; i < hall.InteriorCount; i++) Panels(b, hall.Interior(i), panel, !alongX, alongX);
    }

    /// <summary>
    /// Phase D: walkable stairs. The ground cell row under every step but the last becomes a brick Stair rotated
    /// uphill (its full upper half on the uphill side), so each one-cell drop reads as two half steps. Where a step
    /// passes over a node box's air (its ground would hang in the room), brick is built up under it from the floor.
    /// </summary>
    private static void Stairs(Ctx b, DungeonHall hall)
    {
        byte stair = ShapeCode.Pack(CellShape.Stair, RotationToward(-hall.Dir));
        var from = b.Layout.Rooms[b.Layout.Nodes[hall.FromNode].Room].Interior;
        var to = b.Layout.Rooms[b.Layout.Nodes[hall.ToNode].Room].Interior;
        for (int i = 0; i <= hall.Length; i++)
        {
            var step = hall.Interior(i);
            int ground = step.Min.Y - 1;
            var row = new Aabb3i(new Int3(step.Min.X, ground, step.Min.Z), new Int3(step.Max.X, ground + 1, step.Max.Z));
            Support(b, row, from);
            Support(b, row, to);
            if (i < hall.Length) b.Bp.Fill(row, b.Brick, stair);
        }
    }

    /// <summary>Brick under a stair step's ground row where that row lies inside a room's air.</summary>
    private static void Support(Ctx b, Aabb3i row, Aabb3i room)
    {
        if (row.Min.Y < room.Min.Y) return;
        var column = new Aabb3i(new Int3(row.Min.X, room.Min.Y, row.Min.Z), row.Max).Intersection(room);
        if (!column.IsEmpty) b.Bp.Fill(column, b.Brick);
    }

    /// <summary>Quadrants of a shaft's cross-section in spiral order: each is beside the next and the previous.</summary>
    private static readonly (int X, int Z)[] Spiral = { (0, 0), (1, 0), (1, 1), (0, 1) };

    /// <summary>
    /// Phase D: a shaft you can always climb back up. One-way brick platforms every <see cref="PlatformSpacing"/>
    /// cells (a 4-cell rise, under the player's jump), the first 3 cells over the bottom floor, each covering one
    /// quadrant of the shaft and winding round it like a spiral stair: the next platform up is always beside the one
    /// you stand on and never over the next one down, so you can jump up from one to the next, and walkers can step off
    /// onto the next one down with headroom. The top one is on the side of the node the shaft leaves from, at most 4
    /// cells under that node's floor.
    /// </summary>
    private static void ShaftPlatforms(Ctx b, DungeonHall hall)
    {
        int bottom = hall.End.Y, top = hall.Start.Y;
        if (top - bottom < 5) return;
        var shaft = hall.Interior(0);
        var (alongX, nearPlus) = NearSide(b, hall);
        int half = hall.Width / 2, near = nearPlus ? 1 : 0;
        int t = 0;
        while ((alongX ? Spiral[t].X : Spiral[t].Z) != near) t++;
        int last = (top - bottom - 5) / PlatformSpacing;
        for (int k = 0; k <= last; k++)
        {
            int y = bottom + 3 + k * PlatformSpacing;
            var q = Spiral[(((t + k - last) % 4) + 4) % 4];
            int x0 = shaft.Min.X + q.X * half, z0 = shaft.Min.Z + q.Z * half;
            b.Bp.Fill(new Aabb3i(new Int3(x0, y, z0), new Int3(q.X == 0 ? x0 + half : shaft.Max.X, y + 1, q.Z == 0 ? z0 + half : shaft.Max.Z)), b.Brick, b.Platform);
        }
    }

    /// <summary>The horizontal axis and side of a shaft that face the node it leaves from (the keep's door side for
    /// the entrance shaft, which is centred in the keep).</summary>
    private static (bool AlongX, bool NearPlus) NearSide(Ctx b, DungeonHall hall)
    {
        var room = b.Layout.Rooms[b.Layout.Nodes[hall.FromNode].Room];
        if (room.Kind == RoomKind.Keep) return (b.Layout.Inward.X != 0, b.Layout.Inward.X + b.Layout.Inward.Z > 0);
        int dx = (room.Interior.Min.X + room.Interior.Max.X) - 2 * hall.Start.X;
        int dz = (room.Interior.Min.Z + room.Interior.Max.Z) - 2 * hall.Start.Z;
        return Math.Abs(dx) >= Math.Abs(dz) ? (true, dx > 0) : (false, dz > 0);
    }
}
