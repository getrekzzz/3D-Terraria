using System.Runtime.InteropServices;
using Godot;
using Strata.Core.Meshing;

namespace Strata.Game.World;

/// <summary>Converts Core mesh buffers into Godot ArrayMeshes (main thread).</summary>
public static class MeshUpload
{
    private static readonly Mesh.ArrayFormat CustomFormats =
        (Mesh.ArrayFormat)(((long)Mesh.ArrayCustomFormat.RgbaHalf << (int)Mesh.ArrayFormat.FormatCustom0Shift)
                           | ((long)Mesh.ArrayCustomFormat.Rgba8Unorm << (int)Mesh.ArrayFormat.FormatCustom1Shift));

    public static ArrayMesh? Build(MeshSurface surface, Material material, string name)
    {
        if (surface.IsEmpty) return null;
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        // Straight from the Core lists into packed arrays: no managed copies (they were large-object garbage).
        arrays[(int)Mesh.ArrayType.Vertex] = Variant.CreateFrom(MemoryMarshal.Cast<System.Numerics.Vector3, Vector3>(CollectionsMarshal.AsSpan(surface.Positions)));
        arrays[(int)Mesh.ArrayType.Normal] = Variant.CreateFrom(MemoryMarshal.Cast<System.Numerics.Vector3, Vector3>(CollectionsMarshal.AsSpan(surface.Normals)));
        arrays[(int)Mesh.ArrayType.Color] = Variant.CreateFrom(MemoryMarshal.Cast<System.Numerics.Vector4, Color>(CollectionsMarshal.AsSpan(surface.Colors)));
        arrays[(int)Mesh.ArrayType.Custom0] = Variant.CreateFrom(surface.Custom0ByteSpan);
        arrays[(int)Mesh.ArrayType.Custom1] = Variant.CreateFrom(surface.Custom1ByteSpan);
        arrays[(int)Mesh.ArrayType.Index] = Variant.CreateFrom(CollectionsMarshal.AsSpan(surface.Indices));
        var mesh = new ArrayMesh { ResourceName = name };
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays, null, null, CustomFormats);
        mesh.SurfaceSetMaterial(0, material);
        return mesh;
    }

    public static Vector3[] Faces(System.Collections.Generic.List<System.Numerics.Vector3> triangles) =>
        MemoryMarshal.Cast<System.Numerics.Vector3, Vector3>(CollectionsMarshal.AsSpan(triangles)).ToArray();
}
