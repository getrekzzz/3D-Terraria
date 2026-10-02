using Strata.Core.Common;

namespace Strata.Core.WorldGen.Structures.Dungeon;

/// <summary>
/// Overlap test for planned interiors: a coarse 8-cell grid is the broad phase (which pieces are near), exact box
/// tests are the narrow phase. A piece is a list of interior boxes (a hall's steps, a room). A candidate overlaps when
/// one of its boxes, grown by the required wall gap, intersects a box of a piece it is not deliberately joined to.
/// </summary>
public sealed class OccupancyGrid
{
    private const int Shift = 3; // 8-cell coarse cells
    private readonly Dictionary<long, List<int>> _cells = new();
    private readonly List<Aabb3i[]> _pieces = new();
    private readonly List<int> _tested = new();
    private int _stamp;

    public int Count => _pieces.Count;

    public IReadOnlyList<Aabb3i> Boxes(int piece) => _pieces[piece];

    /// <summary>Registers a piece and returns its id.</summary>
    public int Add(Aabb3i[] boxes)
    {
        int id = _pieces.Count;
        _pieces.Add(boxes);
        _tested.Add(0);
        foreach (var b in boxes)
            ForCells(b, key =>
            {
                if (!_cells.TryGetValue(key, out var list)) _cells[key] = list = new List<int>(2);
                if (list.Count == 0 || list[^1] != id) list.Add(id);
            });
        return id;
    }

    /// <summary>Drops a piece's boxes (it is being replaced); its id stays reserved.</summary>
    public void Retire(int piece) => _pieces[piece] = Array.Empty<Aabb3i>();

    /// <summary>Whether any box (grown by <paramref name="gap"/>) intersects a piece outside <paramref name="joined"/>.</summary>
    public bool Overlaps(IReadOnlyList<Aabb3i> boxes, int gap, IReadOnlyList<int> joined)
    {
        bool hit = false;
        foreach (var raw in boxes)
        {
            var b = raw.Expand(gap);
            int stamp = ++_stamp; // each owner is tested once per candidate box
            ForCells(b, key =>
            {
                if (hit || !_cells.TryGetValue(key, out var owners)) return;
                foreach (int o in owners)
                {
                    if (_tested[o] == stamp) continue;
                    _tested[o] = stamp;
                    if (Contains(joined, o)) continue;
                    foreach (var other in _pieces[o])
                        if (b.Intersects(other)) { hit = true; return; }
                }
            });
            if (hit) return true;
        }
        return false;
    }

    private static bool Contains(IReadOnlyList<int> list, int value)
    {
        for (int i = 0; i < list.Count; i++) if (list[i] == value) return true;
        return false;
    }

    private static void ForCells(Aabb3i b, Action<long> visit)
    {
        if (b.IsEmpty) return;
        for (int y = b.Min.Y >> Shift; y <= (b.Max.Y - 1) >> Shift; y++)
            for (int z = b.Min.Z >> Shift; z <= (b.Max.Z - 1) >> Shift; z++)
                for (int x = b.Min.X >> Shift; x <= (b.Max.X - 1) >> Shift; x++)
                    visit(((long)(x + 0x100000) << 42) | ((long)(y + 0x100000) << 21) | (long)(z + 0x100000));
    }
}
