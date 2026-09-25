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
        Add(visual, MeshUpload.Build(r.Opaque, _materials.Opaque, $"chunk {r.Coord} opaque"), scenario, transform, RenderingServer.ShadowCastingSetting.On);
        Add(visual, MeshUpload.Build(r.Cutout, _materials.Cutout, $"chunk {r.Coord} cutout"), scenario, transform, RenderingServer.ShadowCastingSetting.On);
        Add(visual, MeshUpload.Build(r.Transparent, _materials.Glass, $"chunk {r.Coord} glass"), scenario, transform, RenderingServer.ShadowCastingSetting.Off);
        Add(visual, MeshUpload.Build(r.Liquid, _materials.Liquid, $"chunk {r.Coord} liquid"), scenario, transform, RenderingServer.ShadowCastingSetting.Off);
        visual.Triangles = r.Opaque.TriangleCount + r.Cutout.TriangleCount + r.Transparent.TriangleCount + r.Liquid.TriangleCount;
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
