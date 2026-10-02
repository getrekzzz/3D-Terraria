using Strata.Core.Common;

namespace Strata.Core.Streaming;

/// <summary>
/// Work queues and counters of the streamer. Scheduling walks two small sets instead of every entry each frame:
/// <see cref="_loadQueue"/> (loading entries not yet handed to a worker) and <see cref="_meshCandidates"/> (entries that
/// may need a mesh job). Both are maintained at every state transition and pruned lazily while scheduling, so a
/// settled world schedules without allocating.
/// </summary>
public sealed partial class ChunkStreamer
{
    private readonly HashSet<Int3> _loadQueue = new();
    private readonly HashSet<Int3> _meshCandidates = new();
    private readonly List<(int Priority, Int3 Coord)> _order = new();
    private readonly List<Int3> _prune = new();
    private readonly List<Int3> _unloadScratch = new();

    /// <summary>Bumped whenever a chunk becomes Ready; chunks blocked on neighbours retry only after it changes.</summary>
    private int _readyEpoch;

    /// <summary>Test hook: called with each chunk as its mesh job is scheduled (in scheduling order).</summary>
    internal Action<Int3>? MeshScheduled;

    /// <summary>Priority first, then X, Y, Z, so equal priorities schedule in a deterministic order.</summary>
    private sealed class OrderComparer : IComparer<(int Priority, Int3 Coord)>
    {
        public static readonly OrderComparer Instance = new();

        public int Compare((int Priority, Int3 Coord) a, (int Priority, Int3 Coord) b)
        {
            if (a.Priority != b.Priority) return a.Priority < b.Priority ? -1 : 1;
            if (a.Coord.X != b.Coord.X) return a.Coord.X < b.Coord.X ? -1 : 1;
            if (a.Coord.Y != b.Coord.Y) return a.Coord.Y < b.Coord.Y ? -1 : 1;
            return a.Coord.Z.CompareTo(b.Coord.Z);
        }
    }

    private static bool IsMeshCandidate(Entry e) => e.MeshWanted && e.MeshDirty && !e.MeshInFlight && e.Data == DataState.Ready;

    /// <summary>Call after any transition that may make an entry need a mesh job.</summary>
    private void TouchMesh(Int3 coord, Entry e)
    {
        if (IsMeshCandidate(e)) _meshCandidates.Add(coord);
    }

    /// <summary>Removes an entry and keeps the queues and counters in step.</summary>
    private bool RemoveEntry(Int3 coord, out Entry e)
    {
        if (!_entries.Remove(coord, out e!)) return false;
        _loadQueue.Remove(coord);
        _meshCandidates.Remove(coord);
        if (e.Data == DataState.Ready) _loaded--;
        if (e.HasMesh) _meshed--;
        return true;
    }

    /// <summary>Recounts loaded and meshed entries the slow way (tests compare it with the O(1) counters).</summary>
    internal (int Loaded, int Meshed) RecountForTests()
    {
        int loaded = 0, meshed = 0;
        foreach (var e in _entries.Values)
        {
            if (e.Data == DataState.Ready) loaded++;
            if (e.HasMesh) meshed++;
        }
        return (loaded, meshed);
    }
}
