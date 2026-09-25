using Strata.Core.Common;

namespace Strata.Core.Voxels.TileObjects;

/// <summary>
/// World-level index of placed tile objects: per-chunk ownership (by anchor) plus a cell occupancy map.
/// Main-thread only for writes; per-chunk lists are republished copy-on-write so jobs can read them.
/// </summary>
public sealed class TileObjectStore
{
    private readonly Dictionary<long, TileObjectInstance> _byId = new();
    private readonly Dictionary<Int3, long> _occupancy = new();
    private readonly Dictionary<Int3, TileObjectInstance[]> _byChunk = new();
    private long _nextId = 1;

    public int Count => _byId.Count;

    public event Action<TileObjectInstance>? Added;
    public event Action<TileObjectInstance>? Removed;

    public TileObjectInstance? Get(long id) => _byId.TryGetValue(id, out var o) ? o : null;

    /// <summary>The object occupying a cell, if any.</summary>
    public TileObjectInstance? At(Int3 cell) => _occupancy.TryGetValue(cell, out var id) ? _byId[id] : null;

    public bool IsOccupied(Int3 cell) => _occupancy.ContainsKey(cell);

    public IReadOnlyList<TileObjectInstance> InChunk(Int3 chunk) =>
        _byChunk.TryGetValue(chunk, out var list) ? list : Array.Empty<TileObjectInstance>();

    public IEnumerable<TileObjectInstance> All => _byId.Values;

    /// <summary>Adds an object if all its cells are free. Returns false on overlap.</summary>
    public bool TryAdd(TileObjectInstance obj)
    {
        var b = obj.Bounds;
        for (int y = b.Min.Y; y < b.Max.Y; y++)
            for (int z = b.Min.Z; z < b.Max.Z; z++)
                for (int x = b.Min.X; x < b.Max.X; x++)
                    if (_occupancy.ContainsKey(new Int3(x, y, z))) return false;

        obj.Id = _nextId++;
        _byId[obj.Id] = obj;
        for (int y = b.Min.Y; y < b.Max.Y; y++)
            for (int z = b.Min.Z; z < b.Max.Z; z++)
                for (int x = b.Min.X; x < b.Max.X; x++)
                    _occupancy[new Int3(x, y, z)] = obj.Id;

        var chunk = ChunkMath.CellToChunk(obj.Anchor);
        var old = InChunk(chunk);
        var next = new TileObjectInstance[old.Count + 1];
        for (int i = 0; i < old.Count; i++) next[i] = old[i];
        next[^1] = obj;
        _byChunk[chunk] = next;
        Added?.Invoke(obj);
        return true;
    }

    public bool Remove(TileObjectInstance obj)
    {
        if (!_byId.Remove(obj.Id)) return false;
        var b = obj.Bounds;
        for (int y = b.Min.Y; y < b.Max.Y; y++)
            for (int z = b.Min.Z; z < b.Max.Z; z++)
                for (int x = b.Min.X; x < b.Max.X; x++)
                {
                    var c = new Int3(x, y, z);
                    if (_occupancy.TryGetValue(c, out var id) && id == obj.Id) _occupancy.Remove(c);
                }

        var chunk = ChunkMath.CellToChunk(obj.Anchor);
        var remaining = InChunk(chunk).Where(o => o != obj).ToArray();
        if (remaining.Length == 0) _byChunk.Remove(chunk);
        else _byChunk[chunk] = remaining;
        Removed?.Invoke(obj);
        return true;
    }

    /// <summary>Removes all objects anchored in a chunk (chunk unload). Does not raise events.</summary>
    public IReadOnlyList<TileObjectInstance> DetachChunk(Int3 chunk)
    {
        var list = InChunk(chunk);
        foreach (var obj in list)
        {
            _byId.Remove(obj.Id);
            var b = obj.Bounds;
            for (int y = b.Min.Y; y < b.Max.Y; y++)
                for (int z = b.Min.Z; z < b.Max.Z; z++)
                    for (int x = b.Min.X; x < b.Max.X; x++)
                    {
                        var c = new Int3(x, y, z);
                        if (_occupancy.TryGetValue(c, out var id) && id == obj.Id) _occupancy.Remove(c);
                    }
        }
        _byChunk.Remove(chunk);
        return list;
    }

    /// <summary>Objects whose anchor chunk intersects the given cell box.</summary>
    public IEnumerable<TileObjectInstance> InBox(Aabb3i box)
    {
        var cmin = ChunkMath.CellToChunk(box.Min - Int3.One * 32);
        var cmax = ChunkMath.CellToChunk(box.Max);
        for (int cy = cmin.Y; cy <= cmax.Y; cy++)
            for (int cz = cmin.Z; cz <= cmax.Z; cz++)
                for (int cx = cmin.X; cx <= cmax.X; cx++)
                    foreach (var o in InChunk(new Int3(cx, cy, cz)))
                        if (o.Bounds.Intersects(box)) yield return o;
    }
}
