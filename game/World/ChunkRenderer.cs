using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using Godot;
using Strata.Core.Common;
using Strata.Core.Meshing;

namespace Strata.Game.World;

/// <summary>
/// Owns the render instances of streamed chunks. Mesh results are queued and uploaded within a per-frame
/// time budget (only a chunk's newest mesh; an edit's meshes all at once, first); each chunk has up to four surfaces
/// (opaque, cutout, glass, liquid) as RenderingServer instances (no scene nodes per chunk).
/// </summary>
public sealed partial class ChunkRenderer : Node3D
{
    private sealed class ChunkVisual
    {
        public readonly List<(Rid Instance, ArrayMesh Mesh)> Parts = new();
        public long Triangles;
        public ushort Visibility = ChunkVisibility.AllConnected;
        public bool SkyExposed = true;
        public int OpaqueParts;
        public bool Shown = true;
    }

    private readonly Dictionary<Int3, ChunkVisual> _visuals = new();
    private readonly Queue<ChunkMeshResult> _pending = new();
    private readonly List<ChunkMeshResult> _edits = new();
    // The newest mesh queued for each chunk; older ones still in the queues are skipped.
    private readonly Dictionary<Int3, ChunkMeshResult> _latest = new();
    private TerrainMaterials _materials = null!;

    public int ChunkCount => _visuals.Count;
    public int PendingUploads => _latest.Count;
    public long TriangleCount { get; private set; }

    /// <summary>A chunk's new mesh has replaced its old one (what grows on the terrain switches with it).</summary>
    public event System.Action<ChunkMeshResult>? Uploaded;

    public void Initialize(TerrainMaterials materials) => _materials = materials;

    /// <summary>
    /// Queues a mesh for upload. An edit's meshes (<see cref="ChunkMeshResult.FromEdit"/>, which the streamer hands
    /// over together) all go up on the next <see cref="ProcessUploads"/>, ahead of streaming and regardless of the
    /// budget, so the change appears in one frame.
    /// </summary>
    public void Enqueue(ChunkMeshResult result)
    {
        _latest[result.Coord] = result;
        if (result.FromEdit) _edits.Add(result);
        else _pending.Enqueue(result);
    }

    public void Remove(Int3 coord)
    {
        _latest.Remove(coord); // a mesh still queued for a chunk that left is never uploaded
        if (!_visuals.Remove(coord, out var visual)) return;
        Free(visual);
    }

    /// <summary>Uploads queued meshes: every edit's, then streamed ones until the time budget is used.</summary>
    public int ProcessUploads(double budgetMs)
    {
        var sw = Stopwatch.StartNew();
        int uploaded = 0;
        foreach (var r in _edits)
            if (TryUpload(r)) uploaded++;
        _edits.Clear();
        while (_pending.Count > 0 && (uploaded == 0 || sw.Elapsed.TotalMilliseconds < budgetMs))
            if (TryUpload(_pending.Dequeue())) uploaded++;
        return uploaded;
    }

    /// <summary>Uploads a queued mesh unless a newer one is queued for its chunk (or the chunk was removed).</summary>
    private bool TryUpload(ChunkMeshResult r)
    {
        if (!_latest.TryGetValue(r.Coord, out var latest) || latest != r) return false;
        _latest.Remove(r.Coord);
        Upload(r);
        Uploaded?.Invoke(r);
        return true;
    }

    private void Upload(ChunkMeshResult r)
    {
        var visual = new ChunkVisual();
        var origin = ChunkMath.ChunkOrigin(r.Coord);
        var transform = new Transform3D(Basis.Identity, new Vector3(origin.X, origin.Y, origin.Z) * Units.CellSize);
        var scenario = GetWorld3D().Scenario;
        // Chunks sealed off from the sky cannot shadow anything the sun lights: they sit on their own layer, which the
        // sun and moon skip (see ChunkLayers), and cast at all only while torches cast shadows.
        visual.SkyExposed = r.SkyExposed;
        var casts = CastsShadows(visual) ? RenderingServer.ShadowCastingSetting.On : RenderingServer.ShadowCastingSetting.Off;
        Add(visual, MeshUpload.Build(r.Opaque, _materials.Opaque, $"chunk {r.Coord} opaque"), scenario, transform, casts);
        Add(visual, MeshUpload.Build(r.Cutout, _materials.Cutout, $"chunk {r.Coord} cutout"), scenario, transform, casts);
        visual.OpaqueParts = visual.Parts.Count;
        Add(visual, MeshUpload.Build(r.Transparent, _materials.Glass, $"chunk {r.Coord} glass"), scenario, transform, RenderingServer.ShadowCastingSetting.Off);
        Add(visual, MeshUpload.Build(r.Liquid, _materials.Liquid, $"chunk {r.Coord} liquid"), scenario, transform, RenderingServer.ShadowCastingSetting.Off);
        Add(visual, MeshUpload.Build(r.Lava, _materials.Lava, $"chunk {r.Coord} lava"), scenario, transform, RenderingServer.ShadowCastingSetting.Off);
        visual.Triangles = r.Opaque.TriangleCount + r.Cutout.TriangleCount + r.Transparent.TriangleCount + r.Liquid.TriangleCount + r.Lava.TriangleCount;
        visual.Visibility = r.Visibility;
        _cullDirty = true;
        TriangleCount += visual.Triangles;
        if (_visuals.Remove(r.Coord, out var old)) Free(old);
        _visuals[r.Coord] = visual;
    }

    private static void Add(ChunkVisual visual, ArrayMesh? mesh, Rid scenario, Transform3D transform, RenderingServer.ShadowCastingSetting shadows)
    {
        if (mesh == null) return;
        var instance = RenderingServer.InstanceCreate2(mesh.GetRid(), scenario);
        RenderingServer.InstanceSetTransform(instance, transform);
        RenderingServer.InstanceGeometrySetCastShadowsSetting(instance, shadows);
        if (!visual.SkyExposed) RenderingServer.InstanceSetLayerMask(instance, ChunkLayers.Sealed);
        visual.Parts.Add((instance, mesh));
    }

    private bool _sealedShadows;

    private bool CastsShadows(ChunkVisual v) => v.SkyExposed || _sealedShadows;

    /// <summary>Whether chunks sealed from the sky still cast shadows (needed when torches and other lights cast them).</summary>
    public void SetSealedShadows(bool on)
    {
        if (on == _sealedShadows) return;
        _sealedShadows = on;
        foreach (var v in _visuals.Values)
        {
            if (v.SkyExposed) continue;
            // Opaque and cutout parts are the first two added (glass and liquid never cast).
            for (int i = 0; i < v.Parts.Count; i++)
                if (i < v.OpaqueParts)
                    RenderingServer.InstanceGeometrySetCastShadowsSetting(v.Parts[i].Instance, on ? RenderingServer.ShadowCastingSetting.On : RenderingServer.ShadowCastingSetting.Off);
        }
    }

    private bool _cullDirty = true;
    private Int3 _cullCamera = new(int.MinValue, 0, 0);
    private bool _cullEnabled;

    /// <summary>Chunks hidden by cave culling (tile objects in them are hidden too).</summary>
    public event System.Action<Int3, bool>? ChunkShown;

    public int HiddenCount { get; private set; }

    /// <summary>Triangles in chunks cave culling currently shows.</summary>
    public long ShownTriangles => _visuals.Values.Where(v => v.Shown).Sum(v => v.Triangles);

    /// <summary>
    /// Shows only chunks the camera could see into (see <see cref="ChunkVisibility"/>); recomputed when the camera
    /// changes chunk or meshes arrive.
    /// </summary>
    public void UpdateCulling(Int3 cameraChunk, int radius, int radiusY, bool enabled)
    {
        if (!_cullDirty && cameraChunk == _cullCamera && enabled == _cullEnabled) return;
        _cullDirty = false;
        _cullCamera = cameraChunk;
        _cullEnabled = enabled;
        var visible = enabled
            ? ChunkVisibility.Visible(cameraChunk, c => _visuals.TryGetValue(c, out var v) ? v.Visibility : ChunkVisibility.AllConnected, radius, radiusY)
            : null;
        int hidden = 0;
        foreach (var (coord, visual) in _visuals)
        {
            bool show = visible == null || visible.Contains(coord);
            if (!show) hidden++;
            if (show == visual.Shown) continue;
            visual.Shown = show;
            foreach (var (instance, _) in visual.Parts) RenderingServer.InstanceSetVisible(instance, show);
            ChunkShown?.Invoke(coord, show);
        }
        HiddenCount = hidden;
    }

    /// <summary>Whether cave culling currently shows a chunk (unknown chunks count as shown).</summary>
    public bool IsShown(Int3 coord) => !_visuals.TryGetValue(coord, out var v) || v.Shown;

    private void Free(ChunkVisual visual)
    {
        foreach (var (instance, _) in visual.Parts) RenderingServer.FreeRid(instance);
        TriangleCount -= visual.Triangles;
        visual.Parts.Clear();
    }

    public override void _ExitTree()
    {
        foreach (var v in _visuals.Values) Free(v);
        _visuals.Clear();
    }
}
