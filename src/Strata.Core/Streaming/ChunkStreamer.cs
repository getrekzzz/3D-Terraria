using Strata.Core.Common;
using Strata.Core.Meshing;
using Strata.Core.Session;

namespace Strata.Core.Streaming;

/// <summary>
/// Keeps the chunks around a focus point loaded and meshed. Pipeline per chunk:
/// Loading (worker: load or generate) → Ready (main: installed) → mesh job once all 26 neighbours are
/// Ready → MeshReady event. Chunks outside the range are saved (if modified) and unloaded.
/// Main thread only (jobs run on the <see cref="JobSystem"/>).
/// </summary>
public sealed partial class ChunkStreamer
{
    private enum DataState { Loading, Ready }

    private sealed class Entry
    {
        public DataState Data;
        public bool Cancelled;
        public bool Wanted;
        public bool MeshWanted;
        public bool MeshInFlight;
        public bool MeshDirty = true;
        public bool HasMesh;
        public int Priority;

        /// <summary>The ready epoch at which this chunk last failed <see cref="NeighboursReady"/> (-1: never).</summary>
        public int BlockedEpoch = -1;
    }

    private readonly Dictionary<Int3, Entry> _entries = new();
    private readonly WorldSession _session;
    private readonly JobSystem _jobs;
    private readonly RenderTables _tables;
    private readonly Dictionary<Int3, byte[]> _pendingSaves = new();
    private Int3 _focusChunk = new(int.MinValue, 0, 0);
    private int _loadsInFlight, _meshesInFlight;
    private int _loaded, _meshed;

    public ChunkStreamer(WorldSession session, JobSystem jobs, StreamingSettings settings)
    {
        _session = session;
        _jobs = jobs;
        _tables = session.Render;
        Settings = settings;
    }

    public StreamingSettings Settings { get; }

    /// <summary>A mesh finished for a loaded chunk (render + collide it).</summary>
    public event Action<ChunkMeshResult>? MeshReady;

    /// <summary>A chunk left the loaded set (drop its render/physics objects).</summary>
    public event Action<Int3>? ChunkRemoved;

    public int LoadedCount => _loaded;
    public int MeshedCount => _meshed;
    public int LoadsInFlight => _loadsInFlight;
    public int MeshesInFlight => _meshesInFlight;

    /// <summary>No outstanding loads or meshes for the wanted set.</summary>
    public bool IsIdle => _loadsInFlight == 0 && _meshesInFlight == 0 &&
                          _entries.Values.All(e => !e.MeshWanted || (e.HasMesh && !e.MeshDirty));

    /// <summary>Why streaming is not idle (diagnostics): counts and the first chunk blocked on a missing neighbour.</summary>
    public string DebugState()
    {
        int wanted = 0, dirty = 0, missing = 0, blocked = 0;
        string first = "";
        foreach (var (coord, e) in _entries)
        {
            if (!e.MeshWanted) continue;
            wanted++;
            if (e.MeshDirty) dirty++;
            if (!e.HasMesh)
            {
                missing++;
                if (first.Length == 0) first = $"unmeshed {coord} data={e.Data} dirty={e.MeshDirty} inFlight={e.MeshInFlight}";
            }
            if ((e.MeshDirty || !e.HasMesh) && !e.MeshInFlight && !NeighboursReady(coord))
            {
                blocked++;
                if (first.Length == 0 || first.StartsWith("unmeshed")) first = $"{coord} state={e.Data}";
            }
        }
        return $"wanted={wanted} dirty={dirty} unmeshed={missing} blocked={blocked} {first}";
    }

    public bool IsMeshed(Int3 chunk) => _entries.TryGetValue(chunk, out var e) && e.HasMesh;

    /// <summary>Called every frame with the focus cell (player or camera).</summary>
    public void Update(Int3 focusCell)
    {
        var focus = ChunkMath.CellToChunk(focusCell);
        if (focus != _focusChunk)
        {
            _focusChunk = focus;
            Retarget(focus);
        }
        ScheduleLoads();
        ScheduleMeshes();
        FlushSaves();
    }

    /// <summary>Scheduling priority of urgent block edits (lower runs first).</summary>
    public const int EditPriority = -1000;

    /// <summary>Scheduling priority of urgent light-only remeshes: ahead of distance order, behind block edits.</summary>
    public const int LightPriority = -500;

    /// <summary>
    /// Requests remeshing of chunks (edits, lighting changes). Urgent chunks jump the queue at
    /// <paramref name="urgentPriority"/> (a chunk keeps the more urgent of two requests).
    /// </summary>
    public void MarkDirty(IEnumerable<Int3> chunks, bool urgent = true, int urgentPriority = EditPriority)
    {
        foreach (var c in chunks)
        {
            if (!_entries.TryGetValue(c, out var e)) continue;
            e.MeshDirty = true;
            if (urgent) e.Priority = Math.Min(e.Priority, urgentPriority);
            TouchMesh(c, e);
        }
        if (urgent) ScheduleMeshes();
    }

    /// <summary>Saves and unloads everything (world exit).</summary>
    public void UnloadAll()
    {
        _unloadScratch.Clear();
        _unloadScratch.AddRange(_entries.Keys);
        foreach (var coord in _unloadScratch) Unload(coord);
        _unloadScratch.Clear();
        FlushSaves();
    }
}
