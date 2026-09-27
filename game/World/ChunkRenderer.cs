using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using Godot;
using Strata.Core.Common;
using Strata.Core.Meshing;

namespace Strata.Game.World;

/// <summary>
/// Owns the render instances of streamed chunks. Mesh results are queued and uploaded within a per-frame
/// time budget; each chunk has up to four surfaces (opaque, cutout, glass, liquid) as RenderingServer
/// instances (no scene nodes per chunk).
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
    private TerrainMaterials _materials = null!;

    public int ChunkCount => _visuals.Count;
    public int PendingUploads => _pending.Count;
    public long TriangleCount { get; private set; }

    public void Initialize(TerrainMaterials materials) => _materials = materials;

    public void Enqueue(ChunkMeshResult result) => _pending.Enqueue(result);

    public void Remove(Int3 coord)
    {
        if (!_visuals.Remove(coord, out var visual)) return;
        Free(visual);
    }

    /// <summary>Uploads queued meshes until the time budget is used.</summary>
    public int ProcessUploads(double budgetMs)
    {
        var sw = Stopwatch.StartNew();
        int uploaded = 0;
        while (_pending.Count > 0 && (uploaded == 0 || sw.Elapsed.TotalMilliseconds < budgetMs))
        {
            Upload(_pending.Dequeue());
            uploaded++;
        }
        return uploaded;
    }

    private void Upload(ChunkMeshResult r)
    {
        var visual = new ChunkVisual();
        var origin = ChunkMath.ChunkOrigin(r.Coord);
        var transform = new Transform3D(Basis.Identity, new Vector3(origin.X, origin.Y, origin.Z) * Units.CellSize);
        var scenario = GetWorld3D().Scenario;
        // Chunks sealed off from the sky cannot shadow anything the sun lights: keep them out of the shadow passes
        // (unless torches cast shadows too).
        visual.SkyExposed = r.SkyExposed;
        var casts = CastsShadows(visual) ? RenderingServer.ShadowCastingSetting.On : RenderingServer.ShadowCastingSetting.Off;
        Add(visual, MeshUpload.Build(r.Opaque, _materials.Opaque, $"chunk {r.Coord} opaque"), scenario, transform, casts);
        Add(visual, MeshUpload.Build(r.Cutout, _materials.Cutout, $"chunk {r.Coord} cutout"), scenario, transform, casts);
        visual.OpaqueParts = visual.Parts.Count;
        Add(visual, MeshUpload.Build(r.Transparent, _materials.Glass, $"chunk {r.Coord} glass"), scenario, transform, RenderingServer.ShadowCastingSetting.Off);
        Add(visual, MeshUpload.Build(r.Liquid, _materials.Liquid, $"chunk {r.Coord} liquid"), scenario, transform, RenderingServer.ShadowCastingSetting.Off);
        visual.Triangles = r.Opaque.TriangleCount + r.Cutout.TriangleCount + r.Transparent.TriangleCount + r.Liquid.TriangleCount;
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
