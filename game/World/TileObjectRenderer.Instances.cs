using Godot;
using Strata.Core.Voxels.TileObjects;

namespace Strata.Game.World;

/// <summary>
/// Hiding one drawn object while an effect draws a moving copy in its place (a tree wobbling under the axe), then
/// showing it again. A rewrite of its group (new objects, light changes) shows it early, which only cuts a wobble short.
/// </summary>
public sealed partial class TileObjectRenderer
{
    private static readonly Transform3D Hidden = new(Basis.Identity.Scaled(Vector3.Zero), Vector3.Zero);

    /// <summary>The shared prop models (copies of objects for effects).</summary>
    public Rendering.PropLibrary Props => _props;

    /// <summary>Stops drawing an object (its copy stands in); false if it is not drawn.</summary>
    public bool HideInstance(TileObjectInstance obj)
    {
        if (Find(obj) is not var (g, index)) return false;
        g.Mesh.SetInstanceTransform(index, Hidden);
        return true;
    }

    public void ShowInstance(TileObjectInstance obj)
    {
        if (Find(obj) is var (g, index)) g.Mesh.SetInstanceTransform(index, Placement.TransformOf(obj));
    }

    private (Group Group, int Index)? Find(TileObjectInstance obj)
    {
        if (!_chunks.TryGetValue(ChunkOf(obj), out var state) || !state.Groups.TryGetValue((obj.Def, obj.Variant), out var g)) return null;
        int index = g.Objects.IndexOf(obj);
        return index >= 0 && index < g.Count && GodotObject.IsInstanceValid(g.Mesh) ? (g, index) : null;
    }
}
