using Strata.Core.Common;
using Strata.Core.WorldGen.Structures.Dungeon;

namespace Strata.Core.WorldGen.Structures;

public static partial class DungeonBuilder
{
    private const int PillarRoom = 18, GalleryRoom = 12, GalleryRise = 6, GalleryDepth = 2;

    /// <summary>Phase D for a room: 2 x 2 brick pillars in rooms wider than 18 and a platform gallery along the long
    /// walls of rooms taller than 12.</summary>
    private static void RoomFixtures(Ctx b, DungeonRoom room)
    {
        if (room.Kind is RoomKind.Junction or RoomKind.Alcove) return;
        var r = room.Interior;
        if (Math.Max(r.Size.X, r.Size.Z) > PillarRoom) Pillars(b, room);
        if (r.Size.Y > GalleryRoom) Galleries(b, room);
    }

    /// <summary>Four floor-to-ceiling pillars at the room's quarter points (clear of the centre, the walls, wall-centre
    /// doorways and a shaft arriving through the ceiling); one that would touch a hall, doorway, chest or the Mechanic's
    /// spot is left out.</summary>
    private static void Pillars(Ctx b, DungeonRoom room)
    {
        var r = room.Interior;
        Span<int> xs = stackalloc int[] { r.Min.X + r.Size.X / 4 - 1, r.Max.X - r.Size.X / 4 - 1 };
        Span<int> zs = stackalloc int[] { r.Min.Z + r.Size.Z / 4 - 1, r.Max.Z - r.Size.Z / 4 - 1 };
        foreach (int x in xs)
            foreach (int z in zs)
            {
                var pillar = new Aabb3i(new Int3(x, r.Min.Y, z), new Int3(x + 2, r.Max.Y, z + 2));
                if (Blocked(b, room, pillar.Expand(1))) continue;
                b.Bp.Fill(pillar, b.Brick);
            }
    }

    /// <summary>Whether a box inside a room would block a hall, doorway, chest spot or the Mechanic's spot.</summary>
    private static bool Blocked(Ctx b, DungeonRoom room, Aabb3i box)
    {
        foreach (var d in room.Doorways) if (d.Box.Intersects(box)) return true;
        var node = b.Layout.Nodes[room.Node];
        foreach (var hall in b.Layout.Halls)
        {
            if (hall.FromNode != node.Id && hall.ToNode != node.Id) continue;
            for (int i = 0; i < hall.InteriorCount; i++) if (hall.Interior(i).Intersects(box)) return true;
        }
        foreach (var spot in b.Layout.ChestSpots) if (spot.Room == room.Id && Near(box, spot.Cell)) return true;
        foreach (var spot in b.Layout.VaultChestSpots) if (spot.Room == room.Id && Near(box, spot.Cell)) return true;
        return room.Id == b.Layout.MechanicRoom && Near(box, b.Layout.MechanicCell);
    }

    private static bool Near(Aabb3i box, Int3 cell) => box.Intersects(new Aabb3i(cell - new Int3(1, 0, 1), cell + new Int3(3, 3, 3)));

    /// <summary>
    /// One-way brick platform galleries <see cref="GalleryDepth"/> deep along both long walls at
    /// <see cref="GalleryRise"/> over the floor, broken where a doorway opens in that wall. Each run starts with a
    /// 3 x 2 step platform 4 cells lower so it can be climbed (two 4-cell rises).
    /// </summary>
    private static void Galleries(Ctx b, DungeonRoom room)
    {
        var r = room.Interior;
        bool alongX = r.Size.X >= r.Size.Z;
        int y = r.Min.Y + GalleryRise;
        int a0 = alongX ? r.Min.X : r.Min.Z, a1 = alongX ? r.Max.X : r.Max.Z;
        foreach (int side in new[] { -1, 1 })
        {
            var normal = alongX ? new Int3(0, 0, side) : new Int3(side, 0, 0);
            int w0 = side < 0 ? (alongX ? r.Min.Z : r.Min.X) : (alongX ? r.Max.Z : r.Max.X) - GalleryDepth;
            // Runs of the wall between its doorways (with a cell of margin).
            int run = a0;
            for (int a = a0; a <= a1; a++)
            {
                bool free = a < a1;
                foreach (var d in room.Doorways)
                    if (free && d.Normal == normal && a >= (alongX ? d.Box.Min.X : d.Box.Min.Z) - 1 && a < (alongX ? d.Box.Max.X : d.Box.Max.Z) + 1)
                        free = false;
                if (free) continue;
                if (a - run >= 4)
                {
                    b.Bp.Fill(Strip(alongX, run, a, y, w0, w0 + GalleryDepth), b.Brick, b.Platform);
                    int s0 = side < 0 ? w0 + GalleryDepth : w0 - 2;
                    var step = Strip(alongX, run, run + 3, y - PlatformSpacing, s0, s0 + 2);
                    if (!Blocked(b, room, step)) b.Bp.Fill(step, b.Brick, b.Platform);
                }
                run = a + 1;
            }
        }
    }

    /// <summary>A one-cell-high box spanning [a0, a1) along the gallery axis and [c0, c1) across it.</summary>
    private static Aabb3i Strip(bool alongX, int a0, int a1, int y, int c0, int c1) => alongX
        ? new Aabb3i(new Int3(a0, y, c0), new Int3(a1, y + 1, c1))
        : new Aabb3i(new Int3(c0, y, a0), new Int3(c1, y + 1, a1));
}
