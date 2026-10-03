using Strata.Core.Common;
using Strata.Core.Diagnostics;
using Strata.Core.Voxels;
using Strata.Core.Voxels.TileObjects;

namespace Strata.Core.Gameplay.Housing;

/// <summary>
/// Scanned rooms kept until something near them changes, so the town does not flood-fill every chair's room on every
/// check. Each room is found by any of its interior cells. A block edit (liquids ignored) or a placed or removed tile
/// object drops the rooms whose bounds, grown by the evil margin plus one, touch it. Open results (the fill escaped
/// or ran into unloaded chunks) also expire after <see cref="HousingConfig.OpenRoomTtl"/>. Background callers pass
/// <c>budgeted</c> and get null once <see cref="HousingConfig.MaxScansPerTick"/> new fills ran this tick.
/// </summary>
public sealed class HousingCache : IDisposable
{
    /// <summary>Cached rooms kept at most (a player probing open ground must not grow the cache forever).</summary>
    public const int MaxRooms = 256;

    private readonly HousingScanner _scanner;
    private readonly VoxelWorld _world;
    private readonly Func<long> _tick;
    private readonly Dictionary<Int3, Room> _byCell = new();
    private readonly List<Room> _rooms = new();
    private HousingConfig _config;
    private long _budgetTick = -1;
    private int _budgetUsed;

    public HousingCache(HousingScanner scanner, VoxelWorld world, Func<long> tick)
    {
        _scanner = scanner;
        _world = world;
        _tick = tick;
        _config = scanner.Config;
        world.Edited += OnEdited;
        world.TileObjects.Added += OnObject;
        world.TileObjects.Removed += OnObject;
    }

    /// <summary>Flood fills run by this cache (the house.scans probe).</summary>
    public long Scans { get; private set; }

    public int Count => _rooms.Count;

    /// <summary>The room around a cell: cached, or scanned now (null when a budgeted caller is over this tick's budget).</summary>
    public Room? Get(Int3 cell, bool budgeted = false)
    {
        if (!ReferenceEquals(_config, _scanner.Config)) Clear(); // new rules: every answer may differ
        long tick = _tick();
        if (_byCell.TryGetValue(cell, out var room))
        {
            if (!room.Open || tick - room.ScannedTick < _config.OpenRoomTtl && tick >= room.ScannedTick) return room;
            Drop(room);
        }
        if (budgeted)
        {
            if (_budgetTick != tick)
            {
                _budgetTick = tick;
                _budgetUsed = 0;
            }
            if (_budgetUsed >= _config.MaxScansPerTick) return null;
            _budgetUsed++;
        }
        room = _scanner.Scan(cell);
        room.ScannedTick = tick;
        Scans++;
        Metrics.Global.Count("house.scans");
        if (room.Interior.Count == 0) return room; // not air: nothing to remember it by
        if (_rooms.Count >= MaxRooms) Drop(_rooms[0]); // the oldest answer goes first
        _rooms.Add(room);
        foreach (var c in room.Interior) _byCell[c] = room;
        return room;
    }

    /// <summary>Forgets the room around a cell (a periodic safety re-check of a home).</summary>
    public void Invalidate(Int3 cell)
    {
        if (_byCell.TryGetValue(cell, out var room)) Drop(room);
    }

    public void Clear()
    {
        _byCell.Clear();
        _rooms.Clear();
        _config = _scanner.Config;
    }

    private void OnEdited(EditResult result)
    {
        if (result.Changes.Count == 0 || _rooms.Count == 0) return;
        var min = new Int3(int.MaxValue, int.MaxValue, int.MaxValue);
        var max = new Int3(int.MinValue, int.MinValue, int.MinValue);
        foreach (var ch in result.Changes)
        {
            min = Int3.Min(min, ch.Cell);
            max = Int3.Max(max, ch.Cell);
        }
        DropTouching(new Aabb3i(min, max + Int3.One));
    }

    private void OnObject(TileObjectInstance obj)
    {
        if (_rooms.Count > 0) DropTouching(obj.Bounds);
    }

    private void DropTouching(Aabb3i changed)
    {
        int m = _config.EvilMargin + 1;
        var grow = new Int3(m, m, m);
        for (int i = _rooms.Count - 1; i >= 0; i--)
        {
            var b = _rooms[i].Bounds;
            if (new Aabb3i(b.Min - grow, b.Max + grow).Intersects(changed)) Drop(_rooms[i]);
        }
    }

    private void Drop(Room room)
    {
        _rooms.Remove(room);
        foreach (var c in room.Interior)
            if (_byCell.TryGetValue(c, out var r) && ReferenceEquals(r, room)) _byCell.Remove(c);
    }

    public void Dispose()
    {
        _world.Edited -= OnEdited;
        _world.TileObjects.Added -= OnObject;
        _world.TileObjects.Removed -= OnObject;
    }
}
