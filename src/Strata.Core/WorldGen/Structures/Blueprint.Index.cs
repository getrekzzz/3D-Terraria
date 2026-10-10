using Strata.Core.Common;
using Strata.Core.WorldGen.Chunk;

namespace Strata.Core.WorldGen.Structures;

public sealed partial class Blueprint
{
    private static readonly int[] NoOps = Array.Empty<int>();
    private readonly object _freezeLock = new();
    private volatile Dictionary<Int3, int[]>? _index;

    public bool IsFrozen => _index != null;

    /// <summary>Builds the per-chunk op index and makes the blueprint immutable (idempotent, thread-safe). Each op is
    /// listed under every chunk whose padded generation volume (core plus <see cref="ChunkGenContext.Pad"/>) it
    /// touches; indices are ascending, so op order is preserved.</summary>
    public void Freeze()
    {
        if (_index != null) return;
        lock (_freezeLock)
        {
            if (_index != null) return;
            var lists = new Dictionary<Int3, List<int>>();
            for (int i = 0; i < _ops.Count; i++)
            {
                var b = _ops[i].Bounds;
                if (b.IsEmpty) continue;
                var lo = ChunkMath.CellToChunk(b.Min - Int3.One * ChunkGenContext.Pad);
                var hi = ChunkMath.CellToChunk(b.Max - Int3.One + Int3.One * ChunkGenContext.Pad);
                for (int cy = lo.Y; cy <= hi.Y; cy++)
                    for (int cz = lo.Z; cz <= hi.Z; cz++)
                        for (int cx = lo.X; cx <= hi.X; cx++)
                        {
                            var key = new Int3(cx, cy, cz);
                            if (!lists.TryGetValue(key, out var list)) lists[key] = list = new List<int>();
                            list.Add(i);
                        }
            }
            var index = new Dictionary<Int3, int[]>(lists.Count);
            foreach (var (key, list) in lists) index[key] = list.ToArray();
            _index = index;
        }
    }

    /// <summary>Indices (ascending, into <see cref="Ops"/>) of the ops that may touch a chunk's padded volume; empty
    /// when none. Freezes the blueprint on first use.</summary>
    public int[] OpIndicesFor(Int3 chunk)
    {
        if (_index == null) Freeze();
        return _index!.TryGetValue(chunk, out var ops) ? ops : NoOps;
    }

    /// <summary>The ops that may touch a chunk's padded volume, in original order (empty when none).</summary>
    public BlueprintOp[] OpsFor(Int3 chunk)
    {
        var idx = OpIndicesFor(chunk);
        if (idx.Length == 0) return Array.Empty<BlueprintOp>();
        var ops = new BlueprintOp[idx.Length];
        for (int i = 0; i < idx.Length; i++) ops[i] = _ops[idx[i]];
        return ops;
    }

    /// <summary>Whether a cell lies inside one of the blueprint's zone volumes: any non-object op that is not marked
    /// NoZone.</summary>
    public bool InZone(Int3 cell)
    {
        if (!_bounds.Contains(cell)) return false;
        foreach (int i in OpIndicesFor(ChunkMath.CellToChunk(cell)))
        {
            var op = _ops[i];
            if (op is ObjectOp || op is CarveOp { NoZone: true } || op is FillOp { NoZone: true }) continue;
            if (op.Bounds.Contains(cell)) return true;
        }
        return false;
    }
}
