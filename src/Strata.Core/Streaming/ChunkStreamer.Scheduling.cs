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
        foreach (var (coord, e) in _entries.ToList())
        {
            if (e.Wanted) continue;
            var d = coord - focus;
            bool inHysteresis = Math.Abs(d.X) <= keep && Math.Abs(d.Z) <= keep && d.Y >= -s.MeshDown - 1 - s.Hysteresis && d.Y <= s.MeshUp + 1 + s.Hysteresis;
            if (!inHysteresis) Unload(coord);
        }
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
    }

    private void ScheduleLoads()
    {
        if (_loadsInFlight >= Settings.MaxLoadsInFlight) return;
        foreach (var (coord, e) in _entries.Where(kv => kv.Value.Data == DataState.Loading && kv.Value.Cancelled && kv.Value.Wanted)
                     .OrderBy(kv => kv.Value.Priority).Take(Settings.MaxLoadsInFlight - _loadsInFlight).ToList())
        {
            e.Cancelled = false; // "Cancelled" doubles as "not yet scheduled" for loading entries
            _loadsInFlight++;
            var c = coord;
            _jobs.Schedule(e.Priority, () => _session.Produce(c), product => OnLoaded(c, product), $"load {c}");
        }
    }

    private void OnLoaded(Int3 coord, ChunkProduct? product)
    {
        _loadsInFlight--;
        if (!_entries.TryGetValue(coord, out var e) || e.Data != DataState.Loading || e.Cancelled) return;
        if (product == null)
        {
            // Load failed (already logged): drop the entry; it will be retried if still wanted.
            _entries.Remove(coord);
            _focusChunk = new Int3(int.MinValue, 0, 0);
            return;
        }
        e.Data = DataState.Ready;
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
        if (_meshesInFlight >= Settings.MaxMeshesInFlight) return;
        var candidates = _entries.Where(kv => kv.Value.MeshWanted && kv.Value.MeshDirty && !kv.Value.MeshInFlight && kv.Value.Data == DataState.Ready)
            .OrderBy(kv => kv.Value.Priority).ToList();
        foreach (var (coord, e) in candidates)
        {
            if (_meshesInFlight >= Settings.MaxMeshesInFlight) break;
            if (!NeighboursReady(coord)) continue;
            e.MeshInFlight = true;
            e.MeshDirty = false;
            _meshesInFlight++;
            var c = coord;
            var sources = _session.LightSourcesAround(c);
            _jobs.Schedule(e.Priority, () =>
            {
                var nb = Neighborhood.Capture(_session.World, c);
                return nb == null ? null : ChunkMesher.Build(nb, _tables, _session.Sky.SkyTop, sources, _session.Sky.ShadeTop);
            }, result => OnMeshed(c, result), $"mesh {c}");
            if (e.Priority < 0) e.Priority = 0;
        }
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
            return;
        }
        var chunk = _session.World.GetChunk(coord);
        chunk?.SetLight(result.Light, result.Version);
        if (!e.MeshWanted) return;
        e.HasMesh = true;
        MeshReady?.Invoke(result);
    }

    private void Unload(Int3 coord)
    {
        if (!_entries.Remove(coord, out var e)) return;
        if (e.Data == DataState.Loading) return; // in-flight load result will be discarded
        var blob = _session.Unload(coord);
        if (blob != null) _pendingSaves[coord] = blob;
        if (e.HasMesh) ChunkRemoved?.Invoke(coord);
    }

    private void FlushSaves()
    {
        if (_pendingSaves.Count == 0) return;
        _session.SaveUnloaded(_pendingSaves);
        _pendingSaves.Clear();
    }
}
