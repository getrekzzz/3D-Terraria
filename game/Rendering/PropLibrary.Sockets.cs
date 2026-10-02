using System.Collections.Generic;
using Godot;

namespace Strata.Game.Rendering;

/// <summary>
/// Held-item sockets: the named empties the Blender pipeline exports next to an item's mesh (<c>grip</c>, <c>muzzle</c>,
/// <c>tip</c>, <c>nock</c>, <c>string</c>, <c>chain</c>, <c>handle_end</c>; tools/blender/bgen/sockets.py). They are read
/// once while the model loads (every Node3D below the mesh; the pipeline exports nothing else there) and cached
/// relative to the mesh, so a socket's basis -Z is its forward direction and +Y its
/// up.
/// </summary>
public sealed partial class PropLibrary
{
    private static readonly IReadOnlyDictionary<string, Transform3D> NoSockets = new Dictionary<string, Transform3D>();
    private readonly Dictionary<string, IReadOnlyDictionary<string, Transform3D>> _sockets = new();
    private readonly Dictionary<string, Aabb> _bounds = new();

    /// <summary>The sockets of a model (mesh-relative), empty when it has none or is missing. Loads the model once.</summary>
    public IReadOnlyDictionary<string, Transform3D> Sockets(string projectPath)
    {
        if (!_sockets.ContainsKey(projectPath)) Model(projectPath, multimesh: false);
        return _sockets.TryGetValue(projectPath, out var found) ? found : NoSockets;
    }

    /// <summary>A socket's mesh-relative transform, or null when the model has no socket of that name.</summary>
    public Transform3D? Socket(string projectPath, string name) =>
        Sockets(projectPath).TryGetValue(name, out var t) ? t : null;

    /// <summary>The model's mesh bounds (mesh-relative), or an empty box when it is missing.</summary>
    public Aabb Bounds(string projectPath)
    {
        if (!_bounds.ContainsKey(projectPath)) Model(projectPath, multimesh: false);
        return _bounds.TryGetValue(projectPath, out var box) ? box : new Aabb();
    }

    /// <summary>
    /// A moving part of a model (a chest lid: a child mesh node the pipeline exports with its origin at the hinge,
    /// tools/blender/bgen/mesh.py <c>part</c>) and its rest transform relative to the model's mesh, or null when the
    /// model has none. Loads the model once.
    /// </summary>
    public (Mesh Mesh, Transform3D Hinge)? Part(string projectPath, string name, bool multimesh)
    {
        var key = (projectPath, multimesh);
        if (!_parts.ContainsKey(key)) Model(projectPath, multimesh);
        return _parts.TryGetValue(key, out var parts) && parts != null && parts.TryGetValue(name, out var part) ? part : null;
    }

    private readonly Dictionary<(string, bool), Dictionary<string, (Mesh, Transform3D)>?> _parts = new();

    private void CollectParts((string Path, bool Custom) key, MeshInstance3D mesh, string path)
    {
        Dictionary<string, (Mesh, Transform3D)>? found = null;
        foreach (var child in mesh.GetChildren())
            if (child is MeshInstance3D part && part.Mesh != null)
                (found ??= new())[part.Name] = (Retarget(part.Mesh, key.Custom, path), part.Transform);
        _parts[key] = found;
    }

    private void CollectSockets(string projectPath, MeshInstance3D mesh)
    {
        _bounds[projectPath] = mesh.Mesh.GetAabb();
        Dictionary<string, Transform3D>? found = null;
        Collect(mesh, Transform3D.Identity, ref found);
        _sockets[projectPath] = found ?? NoSockets;
    }

    private static void Collect(Node parent, Transform3D parentToMesh, ref Dictionary<string, Transform3D>? found)
    {
        foreach (var child in parent.GetChildren())
        {
            if (child is not Node3D node || node is MeshInstance3D) continue;
            var toMesh = parentToMesh * node.Transform;
            (found ??= new Dictionary<string, Transform3D>())[node.Name] = toMesh;
            Collect(node, toMesh, ref found);
        }
    }
}
