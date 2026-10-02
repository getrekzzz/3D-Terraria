using Strata.Core.Common;
using Strata.Core.Determinism;

namespace Strata.Core.WorldGen.Structures.Dungeon;

public sealed partial class DungeonPlanner
{
    private readonly List<int> _joined = new();
    private static readonly int[] NoneJoined = Array.Empty<int>();

    private int AddNode(Aabb3i box, RoomKind kind, bool branch)
    {
        int id = _nodes.Count;
        _rooms.Add(new RoomDraft { Interior = box, Kind = kind, Node = id, Piece = _grid.Add(new[] { box }) });
        _nodes.Add(new NodeDraft { Room = _rooms.Count - 1, Branch = branch });
        return id;
    }

    /// <summary>Adds a validated hall and the box at its far end; marks the walls used at both nodes.</summary>
    private int CommitHall(DungeonHall hall, int piece, Aabb3i box, RoomKind kind, bool branch, Int3 placeDir)
    {
        _halls.Add(hall);
        _hallPiece.Add(piece);
        int to = AddNode(box, kind, branch);
        var from = _nodes[hall.FromNode];
        from.UsedDirs |= DirBit(placeDir);
        _nodes[to].UsedDirs |= hall.Kind == HallKind.Shaft ? 16 : DirBit(-placeDir);
        from.Halls.Add(hall.Id);
        _nodes[to].Halls.Add(hall.Id);
        _edges.Add(new DungeonEdge(hall.FromNode, to, hall.Id));
        return to;
    }

    /// <summary>A candidate segment from a node; null when it leaves the world, breaks the surface cover or the floor
    /// limit, or runs into earlier interiors (other than the node it starts from). Also validates a junction box at
    /// its end, returned in <paramref name="box"/>.</summary>
    private DungeonHall? TryHall(int node, HallKind kind, Int3 dir, int len, out Aabb3i box)
    {
        box = default;
        bool shaft = kind == HallKind.Shaft;
        var start = shaft ? ShaftStart(node, dir) : Port(node, dir);
        var hall = new DungeonHall(_halls.Count, start, shaft ? Int3.Down : dir, len, kind, shaft ? ShaftWidth : HallWidth,
            HallHeight, node, _nodes.Count, 0);
        var boxes = hall.Interiors();
        for (int i = 0; i < boxes.Length; i++)
            if (!Fits(boxes[i], kind != HallKind.Stairs || i % SampleStep == 0 || i == len)) return null;
        if (_grid.Overlaps(boxes, WallGap, JoinedAt(node))) return null;
        box = PlaceBox(hall.End, shaft ? Int3.Down : dir, JunctionSize, JunctionSize, JunctionHeight);
        if (!Fits(box, true) || _grid.Overlaps(new[] { box }, WallGap, NoneJoined)) return null;
        return hall;
    }

    /// <summary>Tries a room (instead of a junction) at the end of a just-registered hall.</summary>
    private bool TryRoomBox(DungeonHall hall, int hallPiece, ref Pcg32 rng, out Aabb3i box)
    {
        int along = rng.Range(14, 26), across = rng.Range(14, 26), height = rng.Range(10, 16);
        box = PlaceBox(hall.End, Arrival(hall), along, across, height);
        return Fits(box, true) && !_grid.Overlaps(new[] { box }, WallGap, new[] { hallPiece });
    }

    /// <summary>Grows a dead-end junction into a room (branch ends, room quota).</summary>
    private bool TryUpgrade(int node, ref Pcg32 rng)
    {
        var n = _nodes[node];
        var room = _rooms[n.Room];
        if (n.Halls.Count != 1 || room.Kind != RoomKind.Junction || node == 1) return false;
        var hall = _halls[n.Halls[0]];
        var joined = new[] { _hallPiece[hall.Id], room.Piece };
        for (int attempt = 0; attempt < 5; attempt++)
        {
            bool last = attempt == 4;
            int along = last ? 14 : rng.Range(14, 22), across = last ? 14 : rng.Range(14, 22), height = last ? 10 : rng.Range(10, 14);
            var box = PlaceBox(hall.End, Arrival(hall), along, across, height);
            if (!Fits(box, true) || _grid.Overlaps(new[] { box }, WallGap, joined)) continue;
            _grid.Retire(room.Piece);
            room.Interior = box;
            room.Kind = RoomKind.Hall;
            room.Piece = _grid.Add(new[] { box });
            return true;
        }
        return false;
    }

    /// <summary>Upgrades dead-end junctions until the room quota is met (or none can grow).</summary>
    private void FillRoomQuota(ref Pcg32 rng)
    {
        for (int n = 2; n < _nodes.Count && CountRooms() < _budget.MinRooms; n++)
            TryUpgrade(n, ref rng);
    }

    private int CountRooms()
    {
        int count = 0;
        foreach (var r in _rooms) if (r.Kind is not (RoomKind.Keep or RoomKind.Junction or RoomKind.Alcove)) count++;
        return count;
    }

    /// <summary>Side rooms off 35% of flat halls at least 24 long, behind a 2-cell wall with a 3 x 5 doorway.</summary>
    private void SideRooms(ref Pcg32 rng)
    {
        int count = _halls.Count;
        for (int id = 1; id < count; id++)
        {
            var hall = _halls[id];
            if (hall.Kind != HallKind.Flat || hall.Length < 24 || !rng.Chance(0.35f)) continue;
            int sign = rng.Chance(0.5f) ? 1 : -1;
            int along = rng.Range(8, 12), depth = rng.Range(7, 10), height = rng.Range(8, 10);
            bool alongX = hall.Dir.X != 0;
            int d = alongX ? hall.Dir.X : hall.Dir.Z;
            int m = (alongX ? hall.Start.X : hall.Start.Z) + d * (hall.Length / 2);
            int c = alongX ? hall.Start.Z : hall.Start.X, y = hall.Start.Y;
            int hallLo = c - HallWidth / 2, hallHi = hallLo + HallWidth;
            int r0 = sign > 0 ? hallHi + WallGap : hallLo - WallGap - depth;
            int d0 = sign > 0 ? hallHi : hallLo - WallGap;
            var room = Box(alongX, m - along / 2, m - along / 2 + along, y, y + height, r0, r0 + depth);
            var door = Box(alongX, m - 1, m + 2, y, y + 5, d0, d0 + WallGap);
            if (!Fits(room, true) || _grid.Overlaps(new[] { room }, WallGap, new[] { _hallPiece[id] })) continue;
            int node = AddNode(room, RoomKind.Alcove, _nodes[hall.FromNode].Branch);
            var draft = _rooms[_nodes[node].Room];
            draft.SideHall = id;
            draft.SideDoor = new DungeonDoorway(door, alongX ? new Int3(0, 0, -sign) : new Int3(-sign, 0, 0), id);
            _edges.Add(new DungeonEdge(hall.FromNode, node, id, true));
        }
    }

    /// <summary>A box spanning [a0, a1) along the hall axis and [c0, c1) across it.</summary>
    private static Aabb3i Box(bool alongX, int a0, int a1, int y0, int y1, int c0, int c1) => alongX
        ? new Aabb3i(new Int3(a0, y0, c0), new Int3(a1, y1, c1))
        : new Aabb3i(new Int3(c0, y0, a0), new Int3(c1, y1, a1));

    private static Int3 Arrival(DungeonHall hall) => hall.Kind == HallKind.Shaft ? Int3.Down : hall.Dir;

    /// <summary>A node box for an arrival at floor cell <paramref name="e"/>: a horizontal arrival enters through the
    /// box's near wall (the box extends forward); a shaft arrives through the ceiling (the box is centred).</summary>
    private static Aabb3i PlaceBox(Int3 e, Int3 d, int along, int across, int height)
    {
        int y0 = e.Y, y1 = e.Y + height;
        if (d.Y != 0)
            return new Aabb3i(new Int3(e.X - along / 2, y0, e.Z - across / 2), new Int3(e.X - along / 2 + along, y1, e.Z - across / 2 + across));
        bool alongX = d.X != 0;
        int s = alongX ? e.X : e.Z, step = alongX ? d.X : d.Z, c = alongX ? e.Z : e.X;
        int a0 = step > 0 ? s : s - along + 1;
        return Box(alongX, a0, a0 + along, y0, y1, c - across / 2, c - across / 2 + across);
    }

    /// <summary>Floor cell on the inside of a node box's wall facing <paramref name="dir"/>, centred on that wall.</summary>
    private Int3 Port(int node, Int3 dir)
    {
        var b = _rooms[_nodes[node].Room].Interior;
        int cx = (b.Min.X + b.Max.X) / 2, cz = (b.Min.Z + b.Max.Z) / 2;
        if (dir.X != 0) return new Int3(dir.X > 0 ? b.Max.X - 1 : b.Min.X, b.Min.Y, cz);
        return new Int3(cx, b.Min.Y, dir.Z > 0 ? b.Max.Z - 1 : b.Min.Z);
    }

    /// <summary>Top floor centre of a shaft dug just outside a node box's wall, touching it.</summary>
    private Int3 ShaftStart(int node, Int3 dir)
    {
        var p = Port(node, dir);
        int step = dir.X != 0 ? dir.X : dir.Z, a = dir.X != 0 ? p.X : p.Z;
        int centre = step > 0 ? a + 1 + ShaftWidth / 2 : a - ShaftWidth / 2;
        return dir.X != 0 ? new Int3(centre, p.Y, p.Z) : new Int3(p.X, p.Y, centre);
    }

    private IReadOnlyList<int> JoinedAt(int node)
    {
        _joined.Clear();
        var n = _nodes[node];
        _joined.Add(_rooms[n.Room].Piece);
        foreach (int h in n.Halls) _joined.Add(_hallPiece[h]);
        return _joined;
    }

    /// <summary>Inside the world margin, not below the floor limit and, outside the keep's footprint, at least
    /// <see cref="Cover"/> cells under the ground (sampled every <see cref="SampleStep"/> cells) and under land.</summary>
    private bool Fits(Aabb3i b, bool checkSurface)
    {
        var size = _site.WorldSize;
        if (b.Min.X < Margin || b.Min.Z < Margin || b.Max.X > size.X - Margin || b.Max.Z > size.Z - Margin) return false;
        if (b.Max.Y > size.Y - Margin || b.Min.Y < _floor) return false;
        if (!checkSurface) return true;
        int top = b.Max.Y - 1;
        for (int x = b.Min.X; ; x = Math.Min(x + SampleStep, b.Max.X - 1))
        {
            for (int z = b.Min.Z; ; z = Math.Min(z + SampleStep, b.Max.Z - 1))
            {
                bool underKeep = _keepZone.Contains(x, _keepZone.Min.Y, z);
                if (!underKeep && (top > _site.Surface(x, z) - Cover || _site.Landness(x, z) < MinLandness)) return false;
                if (z == b.Max.Z - 1) break;
            }
            if (x == b.Max.X - 1) break;
        }
        return true;
    }
}
