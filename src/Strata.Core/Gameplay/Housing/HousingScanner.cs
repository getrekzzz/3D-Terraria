using Strata.Core.Common;
using Strata.Core.Gameplay.Navigation;
using Strata.Core.Voxels;
using Strata.Core.Voxels.TileObjects;

namespace Strata.Core.Gameplay.Housing;

/// <summary>A scanned room: its air cells, furniture, and whether it is a valid home (with the reasons if not).</summary>
public sealed class Room
{
    public List<string> Problems { get; } = new();
    public bool Valid => Problems.Count == 0;
    public HashSet<Int3> Interior { get; } = new();
    public List<TileObjectInstance> Furniture { get; } = new();

    /// <summary>Stable identity: the lowest interior cell (y, then z, then x).</summary>
    public Int3 Key { get; set; }

    public int FloorCells { get; set; }
    public int Headroom { get; set; }
    public float BuiltBoundary { get; set; }

    /// <summary>Where a resident stands at home (a navigation node inside the room), if any.</summary>
    public Int3? HomeNode { get; set; }

    public Aabb3i Bounds { get; set; }

    /// <summary>The housing query answer: the first problem, or the "suitable" message.</summary>
    public string Message { get; set; } = "";

    /// <summary>The fill stopped early (open to the world, too large, or not air): no measurements.</summary>
    public bool Open { get; set; }

    /// <summary>Clock tick of the scan (set by <see cref="HousingCache"/>).</summary>
    public long ScannedTick { get; set; }

    public override string ToString() => Valid ? $"valid home ({Interior.Count} cells)" : "not a home: " + string.Join(", ", Problems);
}

/// <summary>
/// 3D housing check: flood-fills the air reachable from a cell without passing solid blocks or doors. The room must
/// close before <see cref="HousingConfig.MaxVolume"/> cells, have enough floor and headroom, be mostly player-built,
/// and contain the furniture roles the config asks for (door in the walls, light, table, chair).
/// </summary>
public sealed class HousingScanner
{
    private readonly VoxelWorld _world;
    private readonly VoxelNavigator _nav;

    private readonly HashSet<Int3> _boundary = new();
    private readonly Queue<Int3> _queue = new();
    private readonly Dictionary<(int, int), int> _columns = new();
    private readonly HashSet<long> _seenObjects = new();
    private HousingConfig _config = null!;
    private bool[] _isEvil = Array.Empty<bool>();

    public HousingScanner(VoxelWorld world, HousingConfig? config = null)
    {
        _world = world;
        _nav = new VoxelNavigator(world);
        Config = config ?? new HousingConfig();
    }

    public HousingConfig Config
    {
        get => _config;
        set
        {
            _config = value;
            var blocks = _world.Content.Blocks;
            _isEvil = new bool[blocks.Count];
            for (int i = 0; i < blocks.Count; i++)
                _isEvil[i] = blocks[i].HasTag(value.EvilTag) && !blocks[i].HasTag(value.EvilIgnoreTag);
        }
    }

    public Room Scan(Int3 start)
    {
        var room = new Room();
        if (!IsAir(start)) return Abort(room, "notOpen");
        var boundary = _boundary;
        var queue = _queue;
        boundary.Clear();
        queue.Clear();
        room.Interior.Add(start);
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            var c = queue.Dequeue();
            foreach (var o in Faces.Offsets)
            {
                var n = c + o;
                if (room.Interior.Contains(n) || boundary.Contains(n)) continue;
                if (_world.GetBlock(n) == VoxelWorld.Unknown || !_world.InBounds(n.X, n.Y, n.Z))
                    return Abort(room, "notEnclosed");
                if (!IsAir(n))
                {
                    boundary.Add(n);
                    continue;
                }
                room.Interior.Add(n);
                if (room.Interior.Count > Config.MaxVolume) return Abort(room, "tooLarge");
                queue.Enqueue(n);
            }
        }
        Measure(room, boundary);
        Check(room, boundary);
        room.Message = room.Valid ? Config.Message("suitable") : room.Problems[0];
        return room;
    }

    /// <summary>The fill stopped early: the explored cells' bounds still locate the result for cache invalidation.</summary>
    private Room Abort(Room room, string key)
    {
        room.Open = true;
        room.Problems.Add(Config.Message(key));
        room.Message = room.Problems[0];
        var min = new Int3(int.MaxValue, int.MaxValue, int.MaxValue);
        var max = new Int3(int.MinValue, int.MinValue, int.MinValue);
        foreach (var c in room.Interior)
        {
            min = Int3.Min(min, c);
            max = Int3.Max(max, c);
        }
        room.Bounds = room.Interior.Count == 0 ? default : new Aabb3i(min, max + Int3.One);
        return room;
    }

    /// <summary>Corruption or Crimson blocks around the room (every other column sampled) reaching the limit.</summary>
    private bool IsCorrupted(Room room)
    {
        int m = Config.EvilMargin, hits = 0;
        var b = room.Bounds;
        for (int y = b.Min.Y - m; y < b.Max.Y + m; y++)
            for (int z = b.Min.Z - m; z < b.Max.Z + m; z += 2)
                for (int x = b.Min.X - m; x < b.Max.X + m; x += 2)
                {
                    ushort id = _world.GetBlock(new Int3(x, y, z));
                    if (id < _isEvil.Length && _isEvil[id] && ++hits >= Config.EvilLimit) return true;
                }
        return false;
    }

    /// <summary>Air for housing: no solid block, and no door (doors belong to the walls).</summary>
    private bool IsAir(Int3 c)
    {
        if (_world.IsSolid(c.X, c.Y, c.Z)) return false;
        return _world.TileObjects.At(c) is not { IsDoor: true };
    }

    private void Measure(Room room, HashSet<Int3> boundary)
    {
        var min = new Int3(int.MaxValue, int.MaxValue, int.MaxValue);
        var max = new Int3(int.MinValue, int.MinValue, int.MinValue);
        var columns = _columns;
        columns.Clear();
        var key = new Int3(int.MaxValue, int.MaxValue, int.MaxValue);
        foreach (var c in room.Interior)
        {
            min = Int3.Min(min, c);
            max = Int3.Max(max, c);
            if (c.Y < key.Y || (c.Y == key.Y && (c.Z < key.Z || (c.Z == key.Z && c.X < key.X)))) key = c;
            if (!room.Interior.Contains(c + Int3.Down) && _world.IsSolid(c.X, c.Y - 1, c.Z)) room.FloorCells++;
            columns[(c.X, c.Z)] = columns.GetValueOrDefault((c.X, c.Z)) + 1;
        }
        room.Bounds = new Aabb3i(min, max + Int3.One);
        int headroom = 0;
        foreach (int h in columns.Values) headroom = Math.Max(headroom, h);
        room.Headroom = headroom;
        room.Key = key;
        int built = 0;
        foreach (var c in boundary)
            if ((_world.GetFlags(c.X, c.Y, c.Z) & CellFlags.PlayerPlaced) != 0 || _world.TileObjects.IsOccupied(c)) built++;
        room.BuiltBoundary = boundary.Count == 0 ? 0 : built / (float)boundary.Count;

        var seen = _seenObjects;
        seen.Clear();
        foreach (var c in room.Interior)
            if (_world.TileObjects.At(c) is { } obj && seen.Add(obj.Id)) room.Furniture.Add(obj);
        foreach (var c in boundary)
            if (_world.TileObjects.At(c) is { } obj && seen.Add(obj.Id)) room.Furniture.Add(obj);
        room.HomeNode = FindHomeNode(room);
    }

    private void Check(Room room, HashSet<Int3> boundary)
    {
        if (room.FloorCells < Config.MinFloorCells) room.Problems.Add(Config.Message("tooSmall"));
        if (room.Headroom < Config.MinHeadroom) room.Problems.Add(Config.Message("lowCeiling"));
        if (room.BuiltBoundary < Config.MinBuiltBoundary) room.Problems.Add(Config.Message("natural"));
        if (IsCorrupted(room)) room.Problems.Add(Config.Message("corrupted"));
        foreach (var req in Config.Requirements)
        {
            bool found = room.Furniture.Any(o => Array.IndexOf(o.Def.Housing, req.Role) >= 0 && Placed(o, room, boundary, req.Where));
            if (!found) room.Problems.Add(req.Message);
        }
        if (room.HomeNode == null && room.Problems.Count == 0) room.Problems.Add(Config.Message("noStand"));
    }

    private static bool Placed(TileObjectInstance obj, Room room, HashSet<Int3> boundary, RequirementPlace where)
    {
        var b = obj.Bounds;
        bool inside = false, wall = false;
        for (int y = b.Min.Y; y < b.Max.Y; y++)
            for (int z = b.Min.Z; z < b.Max.Z; z++)
                for (int x = b.Min.X; x < b.Max.X; x++)
                {
                    var c = new Int3(x, y, z);
                    inside |= room.Interior.Contains(c);
                    wall |= boundary.Contains(c);
                }
        return where switch
        {
            RequirementPlace.Inside => inside,
            RequirementPlace.Boundary => wall,
            _ => inside || wall,
        };
    }

    /// <summary>A standing node whose whole body lies inside the room, nearest the room's floor centre.</summary>
    private Int3? FindHomeNode(Room room)
    {
        var agent = NavAgent.Humanoid;
        var b = room.Bounds;
        float cx = (b.Min.X + b.Max.X) * 0.5f, cz = (b.Min.Z + b.Max.Z) * 0.5f;
        Int3? best = null;
        float bestD = float.MaxValue;
        foreach (var c in room.Interior)
        {
            // Nodes are grid vertices; the cell c is the (+x, +z) column of the node at its corner.
            var node = c;
            if (!_nav.IsStandable(node, agent) || !BodyInside(node, agent, room)) continue;
            float d = (node.X - cx) * (node.X - cx) + (node.Z - cz) * (node.Z - cz) + (node.Y - b.Min.Y) * 4f;
            if (d < bestD)
            {
                bestD = d;
                best = node;
            }
        }
        return best;
    }

    private static bool BodyInside(Int3 node, NavAgent agent, Room room)
    {
        for (int y = 0; y < agent.Height; y++)
            for (int dz = -1; dz <= 0; dz++)
                for (int dx = -1; dx <= 0; dx++)
                    if (!room.Interior.Contains(node + new Int3(dx, y, dz))) return false;
        return true;
    }
}
