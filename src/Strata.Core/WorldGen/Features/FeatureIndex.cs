using Strata.Core.Common;

namespace Strata.Core.WorldGen.Features;

/// <summary>Spatial hash of plan features by chunk coordinate. Immutable after construction; thread-safe to read.</summary>
public sealed class FeatureIndex
{
    private readonly Dictionary<Int3, List<WorldFeature>> _byChunk = new();
    private readonly List<WorldFeature> _all = new();

    public IReadOnlyList<WorldFeature> All => _all;

    public void Add(WorldFeature feature)
    {
        _all.Add(feature);
        var b = feature.Bounds;
        if (b.IsEmpty) return;
        var min = ChunkMath.CellToChunk(b.Min);
        var max = ChunkMath.CellToChunk(b.Max - Int3.One);
        for (int y = min.Y; y <= max.Y; y++)
            for (int z = min.Z; z <= max.Z; z++)
                for (int x = min.X; x <= max.X; x++)
                {
                    var key = new Int3(x, y, z);
                    if (!_byChunk.TryGetValue(key, out var list)) _byChunk[key] = list = new List<WorldFeature>();
                    list.Add(feature);
                }
    }

    private static readonly List<WorldFeature> Empty = new();

    public IReadOnlyList<WorldFeature> InChunk(Int3 chunk) => _byChunk.TryGetValue(chunk, out var list) ? list : Empty;

    public IEnumerable<T> InChunk<T>(Int3 chunk) where T : WorldFeature => InChunk(chunk).OfType<T>();

    public IEnumerable<WorldFeature> At(Int3 cell) => InChunk(ChunkMath.CellToChunk(cell)).Where(f => f.Bounds.Contains(cell));

    public IEnumerable<T> OfKind<T>(string kind) where T : WorldFeature => _all.OfType<T>().Where(f => f.Kind == kind);
}
