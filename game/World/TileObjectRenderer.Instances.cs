using System.Collections.Generic;
using Godot;
using Strata.Core.Common;
using Strata.Core.Voxels.TileObjects;

namespace Strata.Game.World;

/// <summary>
/// Where each placed object is drawn (its multimesh and instance), so effects can hide one and draw a moving copy in
/// its place (a tree wobbling under the axe) and then show it again.
/// </summary>
public sealed partial class TileObjectRenderer
{
    private readonly Dictionary<long, (MultiMesh Mesh, int Index, Transform3D Transform)> _instances = new();
    private readonly Dictionary<Int3, List<long>> _chunkIds = new();
    private static readonly Transform3D Hidden = new(Basis.Identity.Scaled(Vector3.Zero), Vector3.Zero);

    /// <summary>The shared prop models (copies of objects for effects).</summary>
    public Rendering.PropLibrary Props => _props;

    private void Track(Int3 chunk, long id, MultiMesh mesh, int index, Transform3D transform)
    {
        _instances[id] = (mesh, index, transform);
        if (!_chunkIds.TryGetValue(chunk, out var ids)) _chunkIds[chunk] = ids = new List<long>();
        ids.Add(id);
    }

    private void Untrack(Int3 chunk)
    {
        if (!_chunkIds.Remove(chunk, out var ids)) return;
        foreach (var id in ids) _instances.Remove(id);
    }

    /// <summary>Stops drawing an object (its copy stands in); false if it is not drawn.</summary>
    public bool HideInstance(TileObjectInstance obj)
    {
        if (!_instances.TryGetValue(obj.Id, out var i) || !GodotObject.IsInstanceValid(i.Mesh)) return false;
        i.Mesh.SetInstanceTransform(i.Index, Hidden);
        return true;
    }

    public void ShowInstance(TileObjectInstance obj)
    {
        if (_instances.TryGetValue(obj.Id, out var i) && GodotObject.IsInstanceValid(i.Mesh)) i.Mesh.SetInstanceTransform(i.Index, i.Transform);
    }
}
