using System.Diagnostics;
using Strata.Core.Common;
using Strata.Core.Meshing;

namespace Strata.Core.Streaming;

/// <summary>
/// Edits appear whole. A dug block remeshes every chunk within reach of the smoothing and light around it (eight or
/// more), and those finish on different workers frames apart: shown as each lands, the hole opens in one chunk while
/// its neighbour still draws the old ground, the seams between them crack, and the light changes a chunk at a time.
/// An edit's meshes are held until all of its chunks are done, then handed over together (marked
/// <see cref="ChunkMeshResult.FromEdit"/>) so the renderer can switch them in the same frame.
/// </summary>
public sealed partial class ChunkStreamer
{
    /// <summary>Chunks being remeshed for one edit, or for several that overlapped before their meshes were done.</summary>
    private sealed class EditGroup
    {
        public readonly HashSet<Int3> Chunks = new();
        public readonly Dictionary<Int3, ChunkMeshResult> Held = new();
        public long Started = Stopwatch.GetTimestamp();
    }

    private readonly List<EditGroup> _edits = new();

    /// <summary>Puts an edited chunk into <paramref name="group"/> (made on first use), merging any group it is already in.</summary>
    private EditGroup Join(EditGroup? group, Entry e, Int3 coord)
    {
        if (group == null)
        {
            group = e.Edit ?? new EditGroup();
            if (e.Edit == null) _edits.Add(group);
        }
        else if (e.Edit != null && e.Edit != group)
        {
            var other = e.Edit;
            foreach (var c in other.Chunks)
            {
                group.Chunks.Add(c);
                if (_entries.TryGetValue(c, out var oe) && oe.Edit == other) oe.Edit = group;
            }
            foreach (var (c, r) in other.Held) group.Held[c] = r;
            group.Started = Math.Min(group.Started, other.Started);
            _edits.Remove(other);
        }
        group.Chunks.Add(coord);
        e.Edit = group;
        return group;
    }

    /// <summary>Keeps a finished mesh of an edited chunk until the rest of its edit is done.</summary>
    private void Hold(EditGroup group, ChunkMeshResult result)
    {
        result.FromEdit = true;
        group.Held[result.Coord] = result;
        if (Settled(group)) Release(group);
    }

    /// <summary>Hands over edits whose chunks are all done (some may have left the streamed area) or that waited too long.</summary>
    private void ReleaseEdits()
    {
        if (_edits.Count == 0) return;
        foreach (var group in _edits.ToArray())
            if (Settled(group) || Stopwatch.GetElapsedTime(group.Started).TotalMilliseconds > Settings.MaxEditHoldMs) Release(group);
    }

    /// <summary>No chunk of the edit is waiting for a mesh (chunks no longer streamed or meshed do not count).</summary>
    private bool Settled(EditGroup group)
    {
        foreach (var c in group.Chunks)
            if (_entries.TryGetValue(c, out var e) && e.MeshWanted && (e.MeshDirty || e.MeshInFlight)) return false;
        return true;
    }

    private void Release(EditGroup group)
    {
        _edits.Remove(group);
        foreach (var c in group.Chunks)
        {
            // Skip chunks that were unloaded (or unloaded and streamed back in) while the edit was held.
            if (!_entries.TryGetValue(c, out var e) || e.Edit != group) continue;
            e.Edit = null;
            if (!e.MeshWanted || !group.Held.TryGetValue(c, out var result)) continue;
            if (!e.HasMesh) _meshed++;
            e.HasMesh = true;
            MeshReady?.Invoke(result);
        }
    }
}
