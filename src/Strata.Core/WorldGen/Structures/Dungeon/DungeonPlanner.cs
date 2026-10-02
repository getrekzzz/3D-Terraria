using Strata.Core.Common;
using Strata.Core.Determinism;

namespace Strata.Core.WorldGen.Structures.Dungeon;

/// <summary>
/// Plans a Terraria-style dungeon as pure data: a keep on the surface, an entrance shaft, then a long trunk of winding
/// brick halls (flat runs, 1:1 stairs and vertical shafts) heading inland and stepping down toward the caverns, with
/// rooms or junctions at the nodes, branches ending in treasure rooms or libraries, side alcoves off long halls, and
/// room roles by quota. Deterministic integer math; geometry and roles draw from separate named seed streams
/// ("dungeon.trunk", "dungeon.branches", "dungeon.side", "dungeon.roles").
/// </summary>
public sealed partial class DungeonPlanner
{
    private const int HallWidth = 7, HallHeight = 9, ShaftWidth = 8, EntranceWidth = 10;
    private const int JunctionSize = 10, JunctionHeight = 12, EntranceJunction = 16;
    private const int KeepWidth = 20, KeepHeight = 16;
    private const int Margin = 12, Cover = 12, SampleStep = 6, WallGap = 2, Attempts = 7;
    private const float MinLandness = 0.5f;

    private readonly DungeonSite _site;
    private readonly DungeonBudget _budget;
    private readonly int _floor;
    private readonly OccupancyGrid _grid = new();
    private readonly List<DungeonHall> _halls = new();
    private readonly List<int> _hallPiece = new();
    private readonly List<RoomDraft> _rooms = new();
    private readonly List<NodeDraft> _nodes = new();
    private readonly List<DungeonEdge> _edges = new();
    private Aabb3i _keep, _keepZone;
    private int _trunkCells, _branchCells;

    private sealed class NodeDraft
    {
        public int Room;
        public bool Branch;
        public bool BranchEnd;
        public int UsedDirs; // bit per horizontal direction (DirBit) plus bit 4 = entered from above
        public readonly List<int> Halls = new();
    }

    private sealed class RoomDraft
    {
        public Aabb3i Interior;
        public RoomKind Kind;
        public int Node;
        public int Piece;
        public int SideHall = -1; // for side rooms: the hall the doorway opens onto
        public DungeonDoorway SideDoor;
    }

    private DungeonPlanner(DungeonSite site, DungeonBudget budget)
    {
        _site = site;
        _budget = budget;
        _floor = budget.FloorY(site.Bands);
    }

    /// <summary>Plans the dungeon for a site. Never fails: cramped sites get fewer halls and rooms.</summary>
    public static DungeonLayout Plan(DungeonSite site, DungeonBudget budget, SeedTree seeds)
    {
        var planner = new DungeonPlanner(site, budget);
        var trunk = seeds.Rng("dungeon.trunk");
        var branches = seeds.Rng("dungeon.branches");
        var side = seeds.Rng("dungeon.side");
        var roles = seeds.Rng("dungeon.roles");
        planner.PlaceKeep();
        planner.WalkTrunk(ref trunk);
        planner.Branch(ref branches);
        planner.FillRoomQuota(ref branches);
        planner.SideRooms(ref side);
        return planner.Finish(ref roles);
    }

    private void PlaceKeep()
    {
        var g = _site.Gate;
        int floor = g.Y + 1;
        _keep = new Aabb3i(new Int3(g.X - KeepWidth / 2, floor, g.Z - KeepWidth / 2), new Int3(g.X + KeepWidth / 2, floor + KeepHeight, g.Z + KeepWidth / 2));
        _keepZone = _keep.Expand(8);
        AddNode(_keep, RoomKind.Keep, branch: false);

        // Entrance shaft from the keep floor to the first junction, deep enough to clear the lowest ground nearby.
        int lowest = int.MaxValue;
        for (int x = _keep.Min.X - 4; x <= _keep.Max.X + 4; x += 4)
            for (int z = _keep.Min.Z - 4; z <= _keep.Max.Z + 4; z += 4)
                lowest = Math.Min(lowest, _site.Surface(x, z));
        int bottom = Math.Max(Math.Min(floor - 20, lowest - Cover - JunctionHeight - 4), _floor);
        var shaft = new DungeonHall(0, new Int3(g.X, floor, g.Z), Int3.Down, floor - bottom, HallKind.Shaft, EntranceWidth, HallHeight, 0, 1, 0);
        int piece = _grid.Add(shaft.Interiors());
        var box = PlaceBox(shaft.End, Int3.Down, EntranceJunction, EntranceJunction, JunctionHeight);
        CommitHall(shaft, piece, box, RoomKind.Junction, false, Int3.Down);
    }

    /// <summary>The trunk: a walk from the entrance junction. When it gets stuck short of its length (or of the room
    /// quota) it resumes from an earlier trunk node, which forks the trunk instead of leaving a stub dungeon.</summary>
    private void WalkTrunk(ref Pcg32 rng)
    {
        int target = _budget.HallCells;
        _trunkCells = Walk(1, target, _site.Inward, ref rng, branch: false, out int last);
        for (int restart = 0; restart < 8 && _trunkCells < target * 3 / 2; restart++)
        {
            bool shortHalls = _trunkCells < target;
            if (!shortHalls && CountRooms() >= _budget.MinRooms) break;
            int from = shortHalls ? 1 + rng.NextInt(_nodes.Count - 1) : last;
            var heading = PickFreeDir(ref rng, from);
            if (heading == Int3.Zero) continue;
            _trunkCells += Walk(from, shortHalls ? target - _trunkCells : target / 4, heading, ref rng, branch: false, out int end);
            if (end != from) last = end;
        }
    }

    /// <summary>Branches at trunk nodes (p 0.3 while branches remain), each ending in a room.</summary>
    private void Branch(ref Pcg32 rng)
    {
        int left = _budget.Branches, trunkNodes = _nodes.Count;
        for (int n = 1; n < trunkNodes && left > 0; n++)
        {
            if (!rng.Chance(0.3f)) continue;
            var heading = PickFreeDir(ref rng, n);
            if (heading == Int3.Zero) continue;
            int cells = Walk(n, rng.Range(80, 160), heading, ref rng, branch: true, out int last);
            if (cells == 0) continue;
            left--;
            _branchCells += cells;
            if (_rooms[_nodes[last].Room].Kind == RoomKind.Junction) TryUpgrade(last, ref rng);
            _nodes[last].BranchEnd = _rooms[_nodes[last].Room].Kind != RoomKind.Junction;
        }
    }

    /// <summary>Walks from a node, laying hall segments until the cell target is met or nothing fits. Returns the cells
    /// laid and the last node reached.</summary>
    private int Walk(int node, int target, Int3 heading, ref Pcg32 rng, bool branch, out int last)
    {
        int cells = 0;
        last = node;
        while (cells < target)
        {
            DungeonHall? hall = null;
            Aabb3i box = default;
            Int3 dir = Int3.Zero;
            for (int attempt = 0; attempt < Attempts && hall == null; attempt++)
            {
                var kind = PickKind(ref rng, node, branch ? 1f : (float)(_trunkCells + cells) / _budget.HallCells);
                dir = PickDir(ref rng, heading, node);
                int len = PickLength(ref rng, kind, node);
                if (dir == Int3.Zero || len <= 0) continue;
                hall = TryHall(node, kind, dir, len, out box);
            }
            if (hall == null) break;
            int piece = _grid.Add(hall.Interiors());
            // Rooms at 45% of nodes, more while the trunk is behind on its room quota.
            bool behind = !branch && CountRooms() * _budget.HallCells < _budget.MinRooms * (_trunkCells + cells + hall.Length);
            var kindAt = RoomKind.Junction;
            if (rng.Chance(behind ? 0.8f : 0.45f) && TryRoomBox(hall, piece, ref rng, out var roomBox))
            {
                box = roomBox;
                kindAt = RoomKind.Hall;
            }
            node = CommitHall(hall, piece, box, kindAt, branch, dir);
            cells += hall.Length;
            heading = dir;
            last = node;
        }
        return cells;
    }

    /// <summary>Flat 50%, stairs 35%, shaft 15% while above the floor limit. The trunk paces its descent: while it is
    /// deeper (as a fraction of the way to the floor) than it is far along its length, it stays level.</summary>
    private HallKind PickKind(ref Pcg32 rng, int node, float progress)
    {
        int r = rng.NextInt(100);
        int y = NodeFloor(node), top = NodeFloor(1);
        if (top > _floor && (float)(top - y) / (top - _floor) > progress + 0.1f) return HallKind.Flat;
        if (r >= 85 && y - 16 >= _floor) return HallKind.Shaft;
        if (r >= 50 && y - 12 >= _floor) return HallKind.Stairs;
        return HallKind.Flat;
    }

    private int PickLength(ref Pcg32 rng, HallKind kind, int node) => kind switch
    {
        HallKind.Flat => rng.Range(16, 40),
        HallKind.Stairs => Math.Min(rng.Range(12, 28), NodeFloor(node) - _floor),
        _ => Math.Min(rng.Range(16, 32), NodeFloor(node) - _floor),
    };

    /// <summary>Keep the heading (50%) or turn 90 degrees either way (25% each); never head outward and never reuse a
    /// wall of the node. Returns zero when the choice is taken.</summary>
    private Int3 PickDir(ref Pcg32 rng, Int3 heading, int node)
    {
        int r = rng.NextInt(4);
        var dir = r < 2 ? heading : r == 2 ? new Int3(-heading.Z, 0, heading.X) : new Int3(heading.Z, 0, -heading.X);
        if (Dot(dir, _site.Inward) < 0) dir = heading;
        return (_nodes[node].UsedDirs & DirBit(dir)) != 0 ? Int3.Zero : dir;
    }

    private Int3 PickFreeDir(ref Pcg32 rng, int node)
    {
        Span<int> free = stackalloc int[4];
        int count = 0;
        for (int i = 0; i < 4; i++)
            if ((_nodes[node].UsedDirs & (1 << i)) == 0 && Dot(Dirs[i], _site.Inward) >= 0) free[count++] = i;
        return count == 0 ? Int3.Zero : Dirs[free[rng.NextInt(count)]];
    }

    private static readonly Int3[] Dirs = { new(1, 0, 0), new(-1, 0, 0), new(0, 0, 1), new(0, 0, -1) };

    private static int DirBit(Int3 d) => d.X > 0 ? 1 : d.X < 0 ? 2 : d.Z > 0 ? 4 : d.Z < 0 ? 8 : 16;

    private static int Dot(Int3 a, Int3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    private int NodeFloor(int node) => _rooms[_nodes[node].Room].Interior.Min.Y;
}
