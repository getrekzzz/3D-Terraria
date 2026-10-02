using Strata.Core.Common;
using Strata.Core.Meshing;
using Strata.Core.Session;

namespace Strata.Core.Streaming;

public sealed partial class ChunkStreamer
{
    private void Retarget(Int3 focus)
    {
        var s = Settings;
        foreach (var e in _entries.Values)
        {
            e.Wanted = false;
            e.MeshWanted = false;
        }

        int dataR = s.MeshRadius + 1;
        for (int dy = -s.MeshDown - 1; dy <= s.MeshUp + 1; dy++)
            for (int dz = -dataR; dz <= dataR; dz++)
                for (int dx = -dataR; dx <= dataR; dx++)
                {
                    var c = focus + new Int3(dx, dy, dz);
                    if (!_session.World.ChunkInBounds(c)) continue;
                    bool mesh = Math.Abs(dx) <= s.MeshRadius && Math.Abs(dz) <= s.MeshRadius && dy >= -s.MeshDown && dy <= s.MeshUp;
                    Want(c, mesh, dx * dx + dz * dz + dy * dy * 2);
                }

        // Column preload: load every chunk from the column top down to the focus so sunlight into
        // nearby shafts and sinkholes is exact.
        var plan = _session.Generator.Plan;
        for (int dz = -s.SkyColumnRadius; dz <= s.SkyColumnRadius; dz++)
            for (int dx = -s.SkyColumnRadius; dx <= s.SkyColumnRadius; dx++)
            {
                int cx = focus.X + dx, cz = focus.Z + dz;
                if (cx < 0 || cz < 0 || cx >= _session.World.ChunkCounts.X || cz >= _session.World.ChunkCounts.Z) continue;
                int top = (plan.ColumnTop[plan.ColumnIndex(cx, cz)] - 1) >> ChunkMath.Shift;
                for (int cy = focus.Y + s.MeshUp + 2; cy <= top; cy++) Want(new Int3(cx, cy, cz), false, 400 + cy);
            }

        int keep = s.MeshRadius + 1 + s.Hysteresis;
        _unloadScratch.Clear();
        foreach (var (coord, e) in _entries)
        {
            if (e.Wanted) continue;
            var d = coord - focus;
            bool inHysteresis = Math.Abs(d.X) <= keep && Math.Abs(d.Z) <= keep && d.Y >= -s.MeshDown - 1 - s.Hysteresis && d.Y <= s.MeshUp + 1 + s.Hysteresis;
            if (!inHysteresis) _unloadScratch.Add(coord);
        }
        foreach (var coord in _unloadScratch) Unload(coord);
        _unloadScratch.Clear();
    }

    private void Want(Int3 c, bool mesh, int priority)
    {
        if (!_entries.TryGetValue(c, out var e))
        {
            e = new Entry { Data = DataState.Loading, Cancelled = true };
            _entries[c] = e;
        }
        e.Wanted = true;
        e.MeshWanted |= mesh;
        e.Priority = priority;
        if (e.Data == DataState.Loading && e.Cancelled) _loadQueue.Add(c);
        TouchMesh(c, e);
    }

    private void ScheduleLoads()
    {
        if (_loadsInFlight >= Settings.MaxLoadsInFlight || _loadQueue.Count == 0) return;
        _order.Clear();
        _prune.Clear();
        foreach (var coord in _loadQueue)
        {
            // Entries leave the queue lazily: unwanted (hysteresis) ones rejoin through Want.
            if (_entries.TryGetValue(coord, out var q) && q.Data == DataState.Loading && q.Cancelled && q.Wanted) _order.Add((q.Priority, coord));
            else _prune.Add(coord);
        }
        foreach (var coord in _prune) _loadQueue.Remove(coord);
        _order.Sort(OrderComparer.Instance);
        int take = Math.Min(_order.Count, Settings.MaxLoadsInFlight - _loadsInFlight);
        for (int i = 0; i < take; i++)
        {
            var coord = _order[i].Coord;
            var e = _entries[coord];
            e.Cancelled = false; // "Cancelled" doubles as "not yet scheduled" for loading entries
            _loadQueue.Remove(coord);
            _loadsInFlight++;
            ScheduleLoadJob(coord, e.Priority);
        }
    }

    private void ScheduleLoadJob(Int3 c, int priority) =>
        _jobs.Schedule(priority, () => _session.Produce(c), product => OnLoaded(c, product), $"load {c}");

    private void OnLoaded(Int3 coord, ChunkProduct? product)
    {
        _loadsInFlight--;
        if (!_entries.TryGetValue(coord, out var e) || e.Data != DataState.Loading || e.Cancelled) return;
        if (product == null)
        {
            // Load failed (already logged): drop the entry; it will be retried if still wanted.
            RemoveEntry(coord, out _);
            _focusChunk = new Int3(int.MinValue, 0, 0);
            return;
        }
        e.Data = DataState.Ready;
        _loaded++;
        _readyEpoch++;
        TouchMesh(coord, e);
        var skyChanges = _session.Install(product);
        if (skyChanges.Count > 0)
        {
            var dirty = new HashSet<Int3>();
            foreach (var change in skyChanges) _session.AddSkyInfluence(dirty, change);
            dirty.Remove(product.Coord);
            MarkDirty(dirty, urgent: false);
        }
    }

    private void ScheduleMeshes()
    {
        if (_meshesInFlight >= Settings.MaxMeshesInFlight || _meshCandidates.Count == 0) return;
        _order.Clear();
        _prune.Clear();
        foreach (var coord in _meshCandidates)
        {
            if (!_entries.TryGetValue(coord, out var q) || !IsMeshCandidate(q)) _prune.Add(coord);
            else if (q.BlockedEpoch != _readyEpoch) _order.Add((q.Priority, coord));
        }
        foreach (var coord in _prune) _meshCandidates.Remove(coord);
        _order.Sort(OrderComparer.Instance);
        for (int i = 0; i < _order.Count && _meshesInFlight < Settings.MaxMeshesInFlight; i++)
        {
            var coord = _order[i].Coord;
            var e = _entries[coord];
            if (!NeighboursReady(coord))
            {
                e.BlockedEpoch = _readyEpoch;
                continue;
            }
            e.MeshInFlight = true;
            e.MeshDirty = false;
            _meshCandidates.Remove(coord);
            _meshesInFlight++;
            MeshScheduled?.Invoke(coord);
            ScheduleMeshJob(coord, e.Priority);
            if (e.Priority < 0) e.Priority = 0;
        }
    }

    // Separate method so the job closure is only allocated for chunks actually scheduled.
    private void ScheduleMeshJob(Int3 c, int priority)
    {
        var sources = _session.LightSourcesAround(c);
        _jobs.Schedule(priority, () =>
        {
            var nb = Neighborhood.Capture(_session.World, c);
            return nb == null ? null : ChunkMesher.Build(nb, _tables, _session.Sky.SkyTop, sources, _session.Sky.ShadeTop);
        }, result => OnMeshed(c, result), $"mesh {c}");
    }

    private bool NeighboursReady(Int3 c)
    {
        for (int dy = -1; dy <= 1; dy++)
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    var n = c + new Int3(dx, dy, dz);
                    if (!_session.World.ChunkInBounds(n)) continue;
                    if (!_entries.TryGetValue(n, out var e) || e.Data != DataState.Ready) return false;
                }
        return true;
    }

    private void OnMeshed(Int3 coord, ChunkMeshResult? result)
    {
        _meshesInFlight--;
        if (!_entries.TryGetValue(coord, out var e)) return;
        e.MeshInFlight = false;
        if (result == null)
        {
            e.MeshDirty = true;
            TouchMesh(coord, e);
            return;
        }
        TouchMesh(coord, e); // dirtied again while the job ran
        var chunk = _session.World.GetChunk(coord);
        chunk?.SetLight(result.Light, result.Version);
        if (!e.MeshWanted) return;
        if (!e.HasMesh) _meshed++;
        e.HasMesh = true;
        MeshReady?.Invoke(result);
    }

    private void Unload(Int3 coord)
    {
        if (!RemoveEntry(coord, out var e)) return;
        if (e.Data == DataState.Loading) return; // in-flight load result will be discarded
        if (_session.Unload(coord) is { } snapshot) _pendingSaves.Add(snapshot);
        if (e.HasMesh) ChunkRemoved?.Invoke(coord);
    }

    private void FlushSaves()
    {
        if (_pendingSaves.Count == 0) return;
        using var profile = Diagnostics.Metrics.Frame.Time("stream.unload_flush");
        _session.SaveUnloaded(_pendingSaves, wait: false);
        _pendingSaves.Clear();
    }
}
