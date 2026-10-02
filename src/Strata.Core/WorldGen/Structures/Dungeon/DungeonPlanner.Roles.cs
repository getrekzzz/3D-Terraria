using Strata.Core.Common;
using Strata.Core.Determinism;

namespace Strata.Core.WorldGen.Structures.Dungeon;

public sealed partial class DungeonPlanner
{
    /// <summary>Assigns roles by quota, reserves chest spots and the Mechanic site, and freezes the layout.</summary>
    private DungeonLayout Finish(ref Pcg32 rng)
    {
        var doorways = new List<DungeonDoorway>[_rooms.Count];
        for (int r = 0; r < _rooms.Count; r++) doorways[r] = Doorways(r);

        var main = new List<int>();
        for (int r = 0; r < _rooms.Count; r++) if (_rooms[r].Kind == RoomKind.Hall) main.Add(r);
        int vault = -1, mechanic = -1;
        foreach (int r in main)
            if (vault < 0 || _rooms[r].Interior.Min.Y < _rooms[vault].Interior.Min.Y) vault = r;
        if (vault >= 0) _rooms[vault].Kind = RoomKind.Vault;
        if (main.Count > 1)
        {
            var depths = new List<int>();
            foreach (int r in main) depths.Add(_rooms[r].Interior.Min.Y);
            depths.Sort();
            int median = depths[depths.Count / 2];
            foreach (int r in main)
                if (r != vault && (mechanic < 0 || Math.Abs(_rooms[r].Interior.Min.Y - median) < Math.Abs(_rooms[mechanic].Interior.Min.Y - median)))
                    mechanic = r;
            _rooms[mechanic].Kind = RoomKind.Mechanic;
        }

        // Side rooms: treasure (40%) or alcoves, which sometimes hold a chest too.
        int chests = 0;
        var alcoveChest = new bool[_rooms.Count];
        for (int r = 0; r < _rooms.Count; r++)
        {
            var room = _rooms[r];
            if (room.SideHall < 0) continue;
            if (rng.Chance(0.4f)) room.Kind = RoomKind.Treasure;
            else alcoveChest[r] = rng.Chance(0.5f);
            if (room.Kind == RoomKind.Treasure || alcoveChest[r]) chests++;
        }

        // Branch ends hold the loot: treasure or a library.
        var open = new List<int>();
        foreach (int r in main)
        {
            if (_rooms[r].Kind != RoomKind.Hall) continue;
            if (_nodes[_rooms[r].Node].BranchEnd) _rooms[r].Kind = rng.Chance(0.5f) ? RoomKind.Treasure : RoomKind.Library;
            else open.Add(r);
            if (_rooms[r].Kind == RoomKind.Treasure) chests++;
        }
        rng.Shuffle(open);
        int libraries = Count(RoomKind.Library), libraryQuota = Math.Max(1, main.Count / 4), spikes = main.Count / 5;
        foreach (int r in open)
        {
            RoomKind kind;
            if (libraries < libraryQuota) { kind = RoomKind.Library; libraries++; }
            else if (spikes > 0) { kind = RoomKind.SpikePit; spikes--; }
            else if (chests < _budget.LockedChests) { kind = RoomKind.Treasure; chests++; }
            else kind = RoomKind.Barracks;
            _rooms[r].Kind = kind;
        }
        // A lone library may have been lost to a treasure branch end: take one back from the loot rooms.
        if (Count(RoomKind.Library) == 0)
            foreach (var want in new[] { RoomKind.Barracks, RoomKind.SpikePit, RoomKind.Treasure })
            {
                int r = _rooms.FindIndex(x => x.Kind == want && x.SideHall < 0);
                if (r < 0) continue;
                _rooms[r].Kind = RoomKind.Library;
                break;
            }

        var spots = ChestSpots(doorways, alcoveChest);
        if (spots.Count > 0)
        {
            int key = rng.NextInt(spots.Count);
            spots[key] = spots[key] with { ShadowKey = true };
        }
        var rooms = new List<DungeonRoom>(_rooms.Count);
        for (int r = 0; r < _rooms.Count; r++)
            rooms.Add(new DungeonRoom(r, _rooms[r].Interior, _rooms[r].Kind, _rooms[r].Node, rng.NextInt(3), doorways[r]));
        var halls = new List<DungeonHall>(_halls.Count);
        var bounds = _keep.Expand(2);
        foreach (var h in _halls)
        {
            halls.Add(h with { PanelStyle = rng.NextInt(3) });
            bounds = bounds.Union(h.Bounds.Expand(2));
        }
        foreach (var r in _rooms) bounds = bounds.Union(r.Interior.Expand(2));
        var nodes = new List<DungeonNode>(_nodes.Count);
        for (int n = 0; n < _nodes.Count; n++) nodes.Add(new DungeonNode(n, rooms[_nodes[n].Room].CenterFloor, _nodes[n].Room, _nodes[n].Branch));

        var inward = _site.Inward;
        var door = inward.X > 0 ? new Int3(_keep.Max.X, _keep.Min.Y, _site.Gate.Z)
            : inward.X < 0 ? new Int3(_keep.Min.X - 1, _keep.Min.Y, _site.Gate.Z)
            : inward.Z > 0 ? new Int3(_site.Gate.X, _keep.Min.Y, _keep.Max.Z)
            : new Int3(_site.Gate.X, _keep.Min.Y, _keep.Min.Z - 1);
        return new DungeonLayout
        {
            KeepInterior = _keep, Door = door, Inward = inward, Halls = halls, Rooms = rooms, Nodes = nodes, Edges = _edges.ToArray(),
            ChestSpots = spots, VaultChestSpots = vault >= 0 ? VaultSpots(rooms[vault]) : Array.Empty<DungeonChestSpot>(),
            MechanicCell = mechanic >= 0 ? rooms[mechanic].CenterFloor : Int3.Zero, MechanicRoom = mechanic, VaultRoom = vault,
            FloorLimit = _floor, TrunkCells = _trunkCells, BranchCells = _branchCells, Bounds = bounds,
        };
    }

    private int Count(RoomKind kind)
    {
        int n = 0;
        foreach (var r in _rooms) if (r.Kind == kind) n++;
        return n;
    }

    /// <summary>Gold-chest spots: the centre of every treasure room, the back wall of treasure side rooms and chest
    /// alcoves, then extra spots in loot rooms until the budget's locked-chest quota is met.</summary>
    private List<DungeonChestSpot> ChestSpots(List<DungeonDoorway>[] doorways, bool[] alcoveChest)
    {
        var spots = new List<DungeonChestSpot>();
        for (int r = 0; r < _rooms.Count; r++)
        {
            var room = _rooms[r];
            if (room.SideHall >= 0)
            {
                if (room.Kind != RoomKind.Treasure && !alcoveChest[r]) continue;
                // Against the back wall, facing the doorway (Normal points from the room toward the hall).
                var n = room.SideDoor.Normal;
                var b = room.Interior;
                var c = new Int3((b.Min.X + b.Max.X) / 2, b.Min.Y, (b.Min.Z + b.Max.Z) / 2);
                var cell = n.X != 0 ? new Int3(n.X > 0 ? b.Min.X : b.Max.X - 1, c.Y, c.Z) : new Int3(c.X, c.Y, n.Z > 0 ? b.Min.Z : b.Max.Z - 1);
                spots.Add(new DungeonChestSpot(cell, r, n, false));
            }
            else if (room.Kind == RoomKind.Treasure)
                spots.Add(new DungeonChestSpot(CentreFloor(room.Interior), r, Facing(doorways[r]), false));
        }
        foreach (var kind in new[] { RoomKind.Treasure, RoomKind.Barracks, RoomKind.Library })
            for (int extra = 0; extra < 2; extra++)
                for (int r = 0; r < _rooms.Count && spots.Count < _budget.LockedChests; r++)
                {
                    var room = _rooms[r];
                    if (room.Kind != kind || room.SideHall >= 0) continue;
                    var c = CentreFloor(room.Interior);
                    int off = extra == 0 ? -5 : 4; // node rooms are at least 14 wide on both axes
                    var cell = room.Interior.Size.X >= room.Interior.Size.Z ? new Int3(c.X + off, c.Y, c.Z) : new Int3(c.X, c.Y, c.Z + off);
                    spots.Add(new DungeonChestSpot(cell, r, Facing(doorways[r]), false));
                }
        return spots;
    }

    private Int3 Facing(List<DungeonDoorway> doors)
    {
        foreach (var d in doors) if (d.Normal.Y == 0) return d.Normal;
        return _site.Inward;
    }

    private static Int3 CentreFloor(Aabb3i b) => new((b.Min.X + b.Max.X) / 2, b.Min.Y, (b.Min.Z + b.Max.Z) / 2);

    /// <summary>Six biome-chest spots against vault walls without doorways (three per wall).</summary>
    private static DungeonChestSpot[] VaultSpots(DungeonRoom vault)
    {
        var b = vault.Interior;
        var walls = new List<Int3>();
        foreach (var n in Dirs)
        {
            bool blocked = false;
            foreach (var d in vault.Doorways) if (d.Normal == n) blocked = true;
            if (!blocked) walls.Add(n);
        }
        var spots = new DungeonChestSpot[6];
        for (int i = 0; i < 6; i++)
        {
            // Two free walls: three chests each; one free wall: six in a row; none: a row down the middle.
            int perWall = walls.Count >= 2 ? 3 : 6;
            var wall = walls.Count == 0 ? Dirs[0] : walls[Math.Min(i / perWall, walls.Count - 1)];
            int slot = i % perWall;
            bool alongZ = wall.X != 0; // a ±X wall runs along Z
            int lo = alongZ ? b.Min.Z : b.Min.X, span = alongZ ? b.Size.Z : b.Size.X;
            int at = lo + 1 + (span - 2) * (2 * slot + 1) / (2 * perWall) - 1;
            int wallCoord = walls.Count == 0 ? (alongZ ? (b.Min.X + b.Max.X) / 2 : (b.Min.Z + b.Max.Z) / 2)
                : wall.X > 0 ? b.Max.X - 1 : wall.X < 0 ? b.Min.X : wall.Z > 0 ? b.Max.Z - 1 : b.Min.Z;
            var cell = alongZ ? new Int3(wallCoord, b.Min.Y, at) : new Int3(at, b.Min.Y, wallCoord);
            spots[i] = new DungeonChestSpot(cell, vault.Id, -wall, false);
        }
        return spots;
    }

    /// <summary>Openings through a room's shell where its halls (and a side room's doorway) cross it.</summary>
    private List<DungeonDoorway> Doorways(int r)
    {
        var room = _rooms[r];
        var list = new List<DungeonDoorway>();
        if (room.SideHall >= 0) list.Add(room.SideDoor);
        var b = room.Interior;
        foreach (var e in _edges)
        {
            if (e.SideDoor || (e.A != room.Node && e.B != room.Node)) continue;
            var hall = _halls[e.Hall];
            for (int face = 0; face < 6; face++)
            {
                var n = face switch { 0 => new Int3(1, 0, 0), 1 => new Int3(-1, 0, 0), 2 => Int3.Up, 3 => Int3.Down, 4 => new Int3(0, 0, 1), _ => new Int3(0, 0, -1) };
                var lo = new Int3(n.X > 0 ? b.Max.X : n.X < 0 ? b.Min.X - WallGap : b.Min.X, n.Y > 0 ? b.Max.Y : n.Y < 0 ? b.Min.Y - WallGap : b.Min.Y,
                    n.Z > 0 ? b.Max.Z : n.Z < 0 ? b.Min.Z - WallGap : b.Min.Z);
                var hi = new Int3(n.X > 0 ? b.Max.X + WallGap : n.X < 0 ? b.Min.X : b.Max.X, n.Y > 0 ? b.Max.Y + WallGap : n.Y < 0 ? b.Min.Y : b.Max.Y,
                    n.Z > 0 ? b.Max.Z + WallGap : n.Z < 0 ? b.Min.Z : b.Max.Z);
                var slab = new Aabb3i(lo, hi);
                var cut = Aabb3i.Empty;
                for (int i = 0; i < hall.InteriorCount; i++) cut = cut.Union(hall.Interior(i).Intersection(slab));
                if (!cut.IsEmpty) list.Add(new DungeonDoorway(cut, n, hall.Id));
            }
        }
        return list;
    }
}
