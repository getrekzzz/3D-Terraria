using Strata.Core.Common;
using Strata.Core.Content;
using Strata.Core.Voxels;
using Strata.Core.Voxels.TileObjects;
using Strata.Core.WorldGen.Structures.Dungeon;

namespace Strata.Core.WorldGen.Structures;

/// <summary>
/// Carves a planned <see cref="DungeonLayout"/> into a blueprint: Terraria's Dungeon as long brick halls lined with
/// dungeon-wall panels, 1:1 stairs, platformed shafts and pillared rooms under a keep on the surface. Ops are emitted
/// in phases so crossings merge into open junctions instead of walling each other off: A shells (every interior grown
/// by 2, brick), B panels (the side faces of every interior grown by 1), C carves (every interior and doorway), D
/// fixtures (stair shapes, shaft platforms, pillars, galleries, chests). Every written cell is flagged
/// <see cref="CellFlags.Structure"/>, which locks it while the dungeon zone is locked. Integer geometry only.
/// </summary>
public static partial class DungeonBuilder
{
    /// <summary>Rise between shaft platforms: 4 cells (2 m) is under the player's 2.4 m jump apex.</summary>
    public const int PlatformSpacing = 4;

    /// <summary>Builds the blueprint. <paramref name="surface"/> is the ground height (top solid cell) of a column,
    /// sampled for the keep's foundation and approach.</summary>
    public static Blueprint Build(ContentDatabase content, DungeonLayout layout, DungeonStyle style, Func<int, int, int> surface)
    {
        var bp = new Blueprint { Flags = CellFlags.Structure };
        var b = new Ctx(bp, content, layout, style, surface);

        // A: brick shells.
        KeepShell(b);
        foreach (var hall in layout.Halls) HallShell(b, hall);
        foreach (var room in layout.Rooms) if (room.Kind != RoomKind.Keep) bp.Fill(room.Interior.Expand(2), style.Brick);

        // B: wall panels on the side faces (floors and ceilings stay brick).
        foreach (var hall in layout.Halls) HallPanels(b, hall);
        foreach (var room in layout.Rooms) if (room.Kind != RoomKind.Keep) Panels(b, room.Interior, style.Panel(room.PanelStyle), true, true);

        // C: carve every interior and doorway.
        KeepCarve(b);
        foreach (var hall in layout.Halls) foreach (var box in hall.Interiors()) bp.Carve(box);
        foreach (var room in layout.Rooms)
        {
            if (room.Kind != RoomKind.Keep) bp.Carve(room.Interior);
            foreach (var door in room.Doorways) bp.Carve(door.Box);
        }

        // D: fixtures.
        foreach (var hall in layout.Halls)
        {
            if (hall.Kind == HallKind.Stairs) Stairs(b, hall);
            else if (hall.Kind == HallKind.Shaft) ShaftPlatforms(b, hall);
        }
        foreach (var room in layout.Rooms) if (room.Kind != RoomKind.Keep) RoomFixtures(b, room);
        KeepFixtures(b);
        var chest = content.TileObjects.Get("gold_chest");
        foreach (var spot in layout.ChestSpots)
        {
            var anchor = ChestAnchor(content, layout, spot);
            if (OnDais(layout, spot))
            {
                // A treasure room's own chest stands on a one-cell brick dais reaching a cell past it.
                var size = TileObjectInstance.FootprintOf(chest, RotationToward(spot.Facing));
                var dais = new Aabb3i(anchor - new Int3(1, 1, 1), anchor + new Int3(size.X + 1, 0, size.Z + 1));
                bp.Fill(dais.Intersection(layout.Rooms[spot.Room].Interior), style.Brick);
            }
            // Every planned spot is a locked Gold Chest (a Golden Key opens one), the Shadow Key's included.
            bp.Object(chest, anchor, RotationToward(spot.Facing), 0, spot.ShadowKey ? "dungeon_chest_shadow_key" : "dungeon_chest", locked: true);
        }
        return bp;
    }

    /// <summary>Shared build state.</summary>
    private sealed class Ctx
    {
        public Ctx(Blueprint bp, ContentDatabase content, DungeonLayout layout, DungeonStyle style, Func<int, int, int> surface)
        {
            Surface = surface;
            Bp = bp;
            Content = content;
            Layout = layout;
            Style = style;
        }

        public Blueprint Bp { get; }
        public ContentDatabase Content { get; }
        public DungeonLayout Layout { get; }
        public DungeonStyle Style { get; }
        public Func<int, int, int> Surface { get; }
        public string Brick => Style.Brick;
        public byte Platform { get; } = ShapeCode.Pack(CellShape.Platform, 0);
    }

    /// <summary>Quarter turns about +Y that point a shape's (or object's) rotation side at a horizontal direction,
    /// matching ShapeGeometry and BlockGrid: rotation 0 faces +Z, 1 faces -X, 2 faces -Z, 3 faces +X.</summary>
    public static int RotationToward(Int3 dir) => dir.Z > 0 ? 0 : dir.X < 0 ? 1 : dir.Z < 0 ? 2 : 3;

    /// <summary>Whether a chest spot is a treasure room's centre spot, which stands on a dais.</summary>
    public static bool OnDais(DungeonLayout layout, DungeonChestSpot spot)
    {
        var room = layout.Rooms[spot.Room];
        return room.Kind == RoomKind.Treasure && spot.Cell == room.CenterFloor;
    }

    /// <summary>Where a planned gold chest stands: the spot's cell, its footprint kept inside the room (on top of the
    /// dais for a treasure room's centre spot).</summary>
    public static Int3 ChestAnchor(ContentDatabase content, DungeonLayout layout, DungeonChestSpot spot)
    {
        var size = TileObjectInstance.FootprintOf(content.TileObjects.Get("gold_chest"), RotationToward(spot.Facing));
        var room = layout.Rooms[spot.Room].Interior;
        int y = room.Min.Y + (OnDais(layout, spot) ? 1 : 0);
        return new Int3(Math.Clamp(spot.Cell.X, room.Min.X, room.Max.X - size.X), y, Math.Clamp(spot.Cell.Z, room.Min.Z, room.Max.Z - size.Z));
    }

    /// <summary>Fills the vertical side faces of <paramref name="interior"/> grown by one cell with a panel block, over
    /// the interior's height. <paramref name="xFaces"/> / <paramref name="zFaces"/> pick the -X/+X and -Z/+Z faces.</summary>
    private static void Panels(Ctx b, Aabb3i interior, string panel, bool xFaces, bool zFaces)
    {
        int y0 = interior.Min.Y, y1 = interior.Max.Y;
        var lo = interior.Min;
        var hi = interior.Max;
        if (xFaces)
        {
            b.Bp.Fill(new Aabb3i(new Int3(lo.X - 1, y0, lo.Z - 1), new Int3(lo.X, y1, hi.Z + 1)), panel);
            b.Bp.Fill(new Aabb3i(new Int3(hi.X, y0, lo.Z - 1), new Int3(hi.X + 1, y1, hi.Z + 1)), panel);
        }
        if (zFaces)
        {
            b.Bp.Fill(new Aabb3i(new Int3(lo.X - 1, y0, lo.Z - 1), new Int3(hi.X + 1, y1, lo.Z)), panel);
            b.Bp.Fill(new Aabb3i(new Int3(lo.X - 1, y0, hi.Z), new Int3(hi.X + 1, y1, hi.Z + 1)), panel);
        }
    }
}
