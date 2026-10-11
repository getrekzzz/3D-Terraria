using System.Collections.Generic;
using Godot;

namespace Strata.Game.Rendering;

/// <summary>
/// Lit attachments (P3-06): nodes carried by the body but built outside it (the held item, worn wings) take the
/// body's voxel light and tint every time <see cref="SetLight"/> / <see cref="SetTint"/> run, so they darken in caves
/// and warm by torches with the body instead of staying sky-lit.
/// </summary>
public sealed partial class CreatureVisual
{
    private readonly List<GeometryInstance3D> _attached = new();
    private static readonly StringName VoxelLightParam = "voxel_light";
    private static readonly StringName TintParam = "tint";

    /// <summary>Attaches a node to a bone; with <paramref name="lit"/> its geometry follows the body's light.</summary>
    public bool AttachToBone(string bone, Node3D node, bool lit)
    {
        if (!AttachToBone(bone, node)) return false;
        if (lit) AddLit(node);
        return true;
    }

    /// <summary>
    /// Lights <paramref name="node"/> (and the geometry under it now) with the body from here on. Lit geometry must
    /// use a shader with the <c>voxel_light</c> instance uniform (props, block items).
    /// </summary>
    public void AddLit(Node3D node)
    {
        Collect(node);
        if (_lit) ApplyLit(_light);
    }

    /// <summary>Stops lighting a node's geometry (call before freeing it, or to re-parent it).</summary>
    public void Detach(Node3D node)
    {
        for (int i = _attached.Count - 1; i >= 0; i--)
        {
            var g = _attached[i];
            if (!IsInstanceValid(g) || g == node || node.IsAncestorOf(g)) _attached.RemoveAt(i);
        }
    }

    /// <summary>Whether <paramref name="node"/>'s geometry is lit with the body.</summary>
    public bool IsLit(Node3D node) => node is GeometryInstance3D g && _attached.Contains(g);

    private void Collect(Node node)
    {
        if (node is GeometryInstance3D g && !_attached.Contains(g)) _attached.Add(g);
        foreach (var child in node.GetChildren()) Collect(child);
    }

    private void ApplyLit(Color light)
    {
        for (int i = _attached.Count - 1; i >= 0; i--)
        {
            var g = _attached[i];
            if (!IsInstanceValid(g)) _attached.RemoveAt(i);
            else g.SetInstanceShaderParameter(VoxelLightParam, light);
        }
    }

    private void TintLit(Color tint)
    {
        foreach (var g in _attached)
            if (IsInstanceValid(g)) g.SetInstanceShaderParameter(TintParam, tint);
    }
}
