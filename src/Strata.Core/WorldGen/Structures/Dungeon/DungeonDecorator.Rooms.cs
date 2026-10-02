using Strata.Core.Common;
using Strata.Core.Content.Defs;
using Strata.Core.Voxels.TileObjects;

namespace Strata.Core.WorldGen.Structures.Dungeon;

public static partial class DungeonDecorator
{
    private static readonly Int3[] Walls = { new(1, 0, 0), new(-1, 0, 0), new(0, 0, 1), new(0, 0, -1) };
    private static readonly string[] StatueIds = { "skeleton_statue", "armor_statue", "angel_statue" };
    private const float BookFill = 0.6f;
    private const int ShelfLength = 6;

    /// <summary>
    /// A room by its role. Libraries: bookcases on the walls, brick shelves at three heights holding books, a table
    /// with a chair and a candle. Treasure: statues and candelabras flanking the chest on its dais, a banner behind it.
    /// Barracks: two tables with chairs, vases, pots, a chandelier. Spike pits: a band of spikes across the middle under
    /// a platform bridge. The vault keeps its six chest spots clear (biome chests) and the Mechanic room keeps the
    /// bound Mechanic's spot clear (the <c>bound_npc</c> site) round a lived-in corner. Every room gathers cobwebs.
    /// </summary>
    private static void Room(Decor d, DungeonRoom room)
    {
        if (room.Kind == RoomKind.Keep) return;
        var r = room.Interior;
        var centre = room.CenterFloor;
        switch (room.Kind)
        {
            case RoomKind.Library:
                AlongWalls(d, room, d.Set("bookcase"), 0, Hold.Floor, d.Rng.Range(3, 6), 1);
                TableSet(d, room, centre, 1, candle: true);
                Shelves(d, room);
                break;
            case RoomKind.Treasure:
                Treasure(d, room);
                break;
            case RoomKind.Barracks:
                TableSet(d, room, centre + new Int3(-4, 0, 0), 2, candle: false);
                TableSet(d, room, centre + new Int3(4, 0, 0), 2, candle: false);
                AlongWalls(d, room, d.Set("vase"), 0, Hold.Floor, 2, 5);
                AlongWalls(d, room, d.Def("pot"), 0, Hold.Floor, 2, 7);
                Chandelier(d, room, centre);
                break;
            case RoomKind.SpikePit:
                SpikePit(d, room);
                break;
            case RoomKind.Vault:
                VaultChests(d);
                Chandelier(d, room, centre);
                AlongWalls(d, room, d.Def(Banner(d)), 5, Hold.Wall, 2, 6);
                break;
            case RoomKind.Mechanic:
                // The bound Mechanic waits at MechanicCell (reserved clear); her corner gets a table, a bookcase and light.
                Chandelier(d, room, centre);
                TableSet(d, room, centre + new Int3(5, 0, 5), 1, candle: true);
                AlongWalls(d, room, d.Set("bookcase"), 0, Hold.Floor, 1, 2);
                AlongWalls(d, room, d.Def(Banner(d)), 5, Hold.Wall, 1, 4);
                break;
            case RoomKind.Hall:
                Chandelier(d, room, centre);
                AlongWalls(d, room, d.Def("pot"), 0, Hold.Floor, 3, 5);
                AlongWalls(d, room, d.Set("lamp"), 0, Hold.Floor, 1, 8);
                break;
            case RoomKind.Alcove:
                AlongWalls(d, room, d.Def("pot"), 0, Hold.Floor, 2, 3);
                break;
        }
        foreach (int sx in Sides)
            foreach (int sz in Sides)
                if (d.Rng.Chance(CobwebChance))
                {
                    int x = sx < 0 ? r.Min.X : r.Max.X - 2, z = sz < 0 ? r.Min.Z : r.Max.Z - 2;
                    d.Cobweb(new Aabb3i(new Int3(x, r.Max.Y - 2, z), new Int3(x + 2, r.Max.Y, z + 2)));
                }
    }

    /// <summary>The keep's foyer: two chandeliers, four banners on the side walls and two statues inside the door.</summary>
    private static void Keep(Decor d)
    {
        var k = d.Layout.KeepInterior;
        var n = d.Layout.Inward;
        var t = n.X != 0 ? new Int3(0, 0, 1) : new Int3(1, 0, 0);
        var inside = d.Layout.Door - n; // the interior cell just inside the door
        int depth = n.X != 0 ? k.Size.X : k.Size.Z;
        foreach (int f in stackalloc[] { depth / 3, depth * 2 / 3 })
        {
            var c = inside - n * f;
            Chandelier(d, k, new Int3(c.X, k.Min.Y, c.Z));
        }
        foreach (int s in Sides)
        {
            var statue = d.Def(StatueIds[d.Rng.NextInt(StatueIds.Length)]);
            var a = inside - n * 2 + t * (s * 6);
            d.TryPlace(statue, Span(a, a - n + t * 1).Min, Facing(-n), Hold.Floor);
        }
        foreach (var wall in new[] { t, -t })
        {
            int placed = 0;
            for (int f = 3; f < depth - 3 && placed < 2; f += 5)
            {
                var c = inside - n * f;
                int across = wall.X != 0 ? (wall.X > 0 ? k.Max.X - 1 : k.Min.X) : (wall.Z > 0 ? k.Max.Z - 1 : k.Min.Z);
                var cell = wall.X != 0 ? new Int3(across, k.Min.Y + 6, c.Z) : new Int3(c.X, k.Min.Y + 6, across);
                var box = Span(cell, cell - n + Int3.Up * 3);
                if (d.TryPlace(d.Def(Banner(d)), box.Min, Facing(-wall), Hold.Wall, wall)) placed++;
            }
        }
    }

    /// <summary>A box spanning two corner cells (inclusive).</summary>
    private static Aabb3i Span(Int3 a, Int3 b) => new(Min(a, b), Max(a, b) + Int3.One);

    /// <summary>Along-wall coordinate range [lo, hi) and the wall's inner column for wall <paramref name="n"/>.</summary>
    private static (int Lo, int Hi, int Inner) WallLine(Aabb3i r, Int3 n) => n.X != 0
        ? (r.Min.Z, r.Max.Z, n.X > 0 ? r.Max.X - 1 : r.Min.X)
        : (r.Min.X, r.Max.X, n.Z > 0 ? r.Max.Z - 1 : r.Min.Z);

    /// <summary>A cell against wall <paramref name="n"/>: <paramref name="a"/> along it, <paramref name="depth"/> in from
    /// it, <paramref name="h"/> over the floor.</summary>
    private static Int3 WallCell(Aabb3i r, Int3 n, int a, int depth, int h)
    {
        int inner = WallLine(r, n).Inner - (n.X + n.Z) * depth;
        return n.X != 0 ? new Int3(inner, r.Min.Y + h, a) : new Int3(a, r.Min.Y + h, inner);
    }

    /// <summary>Places up to <paramref name="count"/> objects against the room's walls (each wall in turn, from a
    /// random start, <paramref name="gap"/> cells apart), facing into the room. Returns how many went in.</summary>
    private static int AlongWalls(Decor d, DungeonRoom room, TileObjectDef def, int h, Hold hold, int count, int gap)
    {
        var r = room.Interior;
        int placed = 0, first = d.Rng.NextInt(4);
        for (int w = 0; w < 4 && placed < count; w++)
        {
            var n = Walls[(first + w) & 3];
            int rot = Facing(-n);
            var size = TileObjectInstance.FootprintOf(def, rot);
            int along = n.X != 0 ? size.Z : size.X, depth = n.X != 0 ? size.X : size.Z;
            var (lo, hi, _) = WallLine(r, n);
            for (int a = lo + 1 + d.Rng.NextInt(3); a + along <= hi - 1 && placed < count; a += along + gap)
            {
                var box = Span(WallCell(r, n, a, 0, h), WallCell(r, n, a + along - 1, depth - 1, h + size.Y - 1));
                if (d.TryPlace(def, box.Min, rot, hold, n, variant: d.Rng.NextInt(def.Variants))) placed++;
            }
        }
        return placed;
    }

    /// <summary>A chandelier hanging from the ceiling near <paramref name="at"/> (a few spots tried).</summary>
    private static void Chandelier(Decor d, DungeonRoom room, Int3 at) => Chandelier(d, room.Interior, at);

    private static void Chandelier(Decor d, Aabb3i r, Int3 at)
    {
        var def = d.Set("chandelier");
        foreach (var o in Offsets)
        {
            var anchor = new Int3(at.X - 1 + o.X, r.Max.Y - def.SizeY, at.Z - 1 + o.Z);
            if (d.TryPlace(def, anchor, 0, Hold.Ceiling)) return;
        }
    }

    private static readonly Int3[] Offsets =
        { new(0, 0, 0), new(3, 0, 0), new(-3, 0, 0), new(0, 0, 3), new(0, 0, -3), new(4, 0, 4), new(-4, 0, -4), new(4, 0, -4), new(-4, 0, 4) };

    /// <summary>A table near <paramref name="at"/> with chairs at its ends facing it and, optionally, a candle on it.</summary>
    private static void TableSet(Decor d, DungeonRoom room, Int3 at, int chairs, bool candle)
    {
        var table = d.Set("table");
        int rot = d.Rng.NextInt(2);
        var size = TileObjectInstance.FootprintOf(table, rot);
        foreach (var o in Offsets)
        {
            var anchor = new Int3(at.X - size.X / 2 + o.X, room.Interior.Min.Y, at.Z - size.Z / 2 + o.Z);
            if (!d.TryPlace(table, anchor, rot, Hold.Floor)) continue;
            var box = new Aabb3i(anchor, anchor + size);
            var chair = d.Set("chair");
            bool alongX = size.X >= size.Z;
            for (int i = 0; i < chairs; i++)
            {
                var dir = (alongX ? new Int3(1, 0, 0) : new Int3(0, 0, 1)) * (i == 0 ? -1 : 1); // from the table to the chair
                int crot = Facing(-dir);
                var cs = TileObjectInstance.FootprintOf(chair, crot);
                int mid = alongX ? box.Min.Z + (size.Z - cs.Z) / 2 : box.Min.X + (size.X - cs.X) / 2;
                var ca = alongX
                    ? new Int3(dir.X < 0 ? box.Min.X - cs.X : box.Max.X, box.Min.Y, mid)
                    : new Int3(mid, box.Min.Y, dir.Z < 0 ? box.Min.Z - cs.Z : box.Max.Z);
                d.TryPlace(chair, ca, crot, Hold.Floor);
            }
            if (candle) d.TryPlace(d.Def("candle"), new Int3(box.Min.X + 1, box.Max.Y, box.Min.Z + 1), 0, Hold.Rests);
            return;
        }
    }

    /// <summary>Brick platform shelves (<see cref="ShelfLength"/> long, against walls with nothing in front) at three
    /// heights, with books on <see cref="BookFill"/> of their cells.</summary>
    private static void Shelves(Decor d, DungeonRoom room)
    {
        var r = room.Interior;
        var book = d.Def("book");
        int first = d.Rng.NextInt(4);
        for (int w = 0; w < 4; w++)
        {
            var n = Walls[(first + w) & 3];
            var (lo, hi, _) = WallLine(r, n);
            for (int a = lo + 1; a + ShelfLength <= hi - 1; a += ShelfLength + 2)
                for (int h = 3; h + 2 < r.Size.Y; h += 3)
                {
                    var shelf = Span(WallCell(r, n, a, 0, h), WallCell(r, n, a + ShelfLength - 1, 0, h));
                    var above = new Aabb3i(shelf.Min + Int3.Up, shelf.Max + Int3.Up);
                    if (!d.Clear(shelf) || !d.Clear(above) || !d.Solid(Decor.Behind(shelf, n))) continue;
                    d.Fill(shelf, d.Style.Brick, d.Platform);
                    for (int i = 0; i < ShelfLength; i++)
                        if (d.Rng.Chance(BookFill))
                            d.TryPlace(book, WallCell(r, n, a + i, 0, h + 1), d.Rng.NextInt(4), Hold.Shelf, variant: d.Rng.NextInt(book.Variants));
                }
        }
    }

    /// <summary>Statues and candelabras either side of the chest on its dais and a banner on the wall behind it.</summary>
    private static void Treasure(Decor d, DungeonRoom room)
    {
        foreach (var spot in d.Layout.ChestSpots)
        {
            if (spot.Room != room.Id || !DungeonBuilder.OnDais(d.Layout, spot)) continue;
            var chest = d.ChestBox(spot);
            var f = spot.Facing;
            var t = f.X != 0 ? new Int3(0, 0, 1) : new Int3(1, 0, 0);
            var floor = new Int3(chest.Min.X, room.Interior.Min.Y, chest.Min.Z);
            var statue = d.Def(StatueIds[d.Rng.NextInt(StatueIds.Length)]);
            var candelabra = d.Set("candelabra");
            foreach (int s in Sides)
            {
                // The dais reaches one cell past the chest; the statue stands beyond it, the candelabra beyond that.
                d.TryPlace(statue, floor + t * (s < 0 ? -3 : 3), Facing(f), Hold.Floor);
                d.TryPlace(candelabra, floor + t * (s < 0 ? -5 : 5), Facing(f), Hold.Floor);
            }
            var back = -f;
            var (lo, hi, _) = WallLine(room.Interior, back);
            int a = f.X != 0 ? chest.Min.Z : chest.Min.X;
            var cell = WallCell(room.Interior, back, Math.Clamp(a, lo, hi - 2), 0, 5);
            var banner = d.Def(Banner(d));
            int rot = Facing(f);
            var size = TileObjectInstance.FootprintOf(banner, rot);
            d.TryPlace(banner, Span(cell, cell + new Int3(size.X - 1, 0, size.Z - 1)).Min, rot, Hold.Wall, back);
        }
        Chandelier(d, room, room.CenterFloor + new Int3(0, 0, 4));
    }

    /// <summary>Two rows of spikes across the middle of the room's long axis under a 3-wide platform bridge.</summary>
    private static void SpikePit(Decor d, DungeonRoom room)
    {
        var r = room.Interior;
        bool alongX = r.Size.X >= r.Size.Z;
        int mid = alongX ? (r.Min.X + r.Max.X) / 2 : (r.Min.Z + r.Max.Z) / 2;
        int c0 = alongX ? r.Min.Z : r.Min.X, c1 = alongX ? r.Max.Z : r.Max.X;
        var spike = d.Def("spike");
        for (int a = mid - 1; a <= mid; a++)
            for (int c = c0; c < c1; c++)
                d.TryPlace(spike, alongX ? new Int3(a, r.Min.Y, c) : new Int3(c, r.Min.Y, a), 0, Hold.Floor);
        int cc = (c0 + c1) / 2, y = r.Min.Y + 3;
        var bridge = alongX
            ? new Aabb3i(new Int3(mid - 3, y, cc - 1), new Int3(mid + 3, y + 1, cc + 2))
            : new Aabb3i(new Int3(cc - 1, y, mid - 3), new Int3(cc + 2, y + 1, mid + 3));
        if (d.Clear(bridge)) d.Fill(bridge, d.Style.Brick, d.Platform);
        Chandelier(d, room, room.CenterFloor + (alongX ? new Int3(5, 0, 0) : new Int3(0, 0, 5)));
    }

    /// <summary>The six biome chests (locked until Plantera falls, each by its hardmode biome key) on the vault's
    /// reserved spots, facing into the room, each rolling its biome's weapon.</summary>
    public static readonly string[] BiomeChests = { "jungle", "corruption", "crimson", "hallowed", "frozen", "desert" };

    private static void VaultChests(Decor d)
    {
        var spots = d.Layout.VaultChestSpots;
        for (int i = 0; i < spots.Count && i < BiomeChests.Length; i++)
        {
            var id = BiomeChests[i] + "_chest";
            var box = d.ChestBox(spots[i]);
            d.TryPlace(d.Def(id), box.Min, DungeonBuilder.RotationToward(spots[i].Facing), Hold.Floor, loot: id, reserved: false, locked: true);
        }
    }

    /// <summary>When no library got a book, one Water Bolt book still waits on a room floor.</summary>
    private static void FallbackBook(Decor d)
    {
        if (d.Books > 0) return;
        var book = d.Def("book");
        foreach (var room in d.Layout.Rooms)
        {
            if (room.Kind is RoomKind.Keep or RoomKind.Junction) continue;
            for (int w = 0; w < 4; w++)
            {
                var n = Walls[w];
                var (lo, hi, _) = WallLine(room.Interior, n);
                for (int a = lo + 1; a < hi - 1; a++)
                    if (d.TryPlace(book, WallCell(room.Interior, n, a, 0, 0), 0, Hold.Floor)) return;
            }
        }
    }
}
