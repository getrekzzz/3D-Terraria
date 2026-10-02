using System.Collections.Generic;
using Godot;
using Strata.Core.Common;
using Strata.Core.Content;
using Strata.Core.Content.Defs;
using Strata.Core.Meshing;

namespace Strata.Game.Items;

/// <summary>
/// Blocks as items (P3-06): a small cube per block, each face textured with the block's own terrain layer (top, side,
/// bottom: <see cref="RenderTables.Surfaces"/>) covering one cell's worth of the texture, drawn with
/// shaders/block_item.gdshader. The terrain texture arrays are shared (the cached resources TerrainMaterials loads);
/// meshes are built once per block and shared by every holder. Lit through the <c>voxel_light</c> instance uniform.
/// </summary>
public static class BlockItemMeshes
{
    /// <summary>Edge length of the cube (m), centred on the origin.</summary>
    public const float Size = 0.2f;

    // Terrain tiling (shaders/include/terrain_common natural/crisp_tile_meters): a face shows one cell of it.
    private const float NaturalTile = 2f, CrispTile = 0.5f;

    private static readonly Dictionary<int, ArrayMesh> Meshes = new();
    private static RenderTables? _tables;
    private static ShaderMaterial? _material;

    /// <summary>The shared block-item material (terrain albedo/material arrays).</summary>
    public static ShaderMaterial Material => _material ??= CreateMaterial();

    /// <summary>The cube for the block an item places, or null when it places none (or the tables lack it).</summary>
    public static ArrayMesh? ForItem(ItemDef item, ContentDatabase content, RenderTables tables)
    {
        if (item.PlaceBlock == null || !content.Blocks.TryGet(item.PlaceBlock, out var block)) return null;
        return ForBlock(block.RuntimeId, tables);
    }

    public static ArrayMesh? ForBlock(int id, RenderTables tables)
    {
        if (!ReferenceEquals(tables, _tables))
        {
            Meshes.Clear(); // a new world's tables: surfaces may have moved
            _tables = tables;
        }
        if (Meshes.TryGetValue(id, out var cached)) return cached;
        if (id <= 0 || id >= tables.Surface.Length || tables.Class[id] == CellClass.Empty) return null;
        int surface = tables.Surface[id];
        var layers = tables.Surfaces[surface];
        float span = Units.CellSize / (tables.Class[id] == CellClass.Crisp ? CrispTile : NaturalTile);
        var mesh = Build(layers, span, tables.SurfaceGlow[surface]);
        Meshes[id] = mesh;
        return mesh;
    }

    private static ShaderMaterial CreateMaterial()
    {
        var m = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/block_item.gdshader") };
        m.SetShaderParameter("albedo_array", GD.Load<TextureLayered>("res://assets/textures/terrain/terrain_albedo.png"));
        m.SetShaderParameter("material_array", GD.Load<TextureLayered>("res://assets/textures/terrain/terrain_material.png"));
        return m;
    }

    // Faces: normal, then the face's right and up axes (texture u along right, v down along up).
    private static readonly (Vector3 N, Vector3 R, Vector3 U)[] Faces =
    {
        (Vector3.Up, Vector3.Right, Vector3.Forward),
        (Vector3.Down, Vector3.Right, Vector3.Back),
        (Vector3.Back, Vector3.Right, Vector3.Up),
        (Vector3.Forward, Vector3.Left, Vector3.Up),
        (Vector3.Right, Vector3.Forward, Vector3.Up),
        (Vector3.Left, Vector3.Back, Vector3.Up),
    };

    private static ArrayMesh Build(SurfaceLayers layers, float span, float glow)
    {
        var verts = new Vector3[24];
        var normals = new Vector3[24];
        var uvs = new Vector2[24];
        var uv2 = new Vector2[24];
        var indices = new int[36];
        float h = Size * 0.5f;
        for (int f = 0; f < 6; f++)
        {
            var (n, r, u) = Faces[f];
            float layer = f == 0 ? layers.Top : f == 1 ? layers.Bottom : layers.Side;
            for (int c = 0; c < 4; c++)
            {
                float sx = c is 1 or 2 ? 1f : -1f, sy = c >= 2 ? 1f : -1f;
                int v = f * 4 + c;
                verts[v] = (n + r * sx + u * sy) * h;
                normals[v] = n;
                uvs[v] = new Vector2((sx + 1f) * 0.5f, (1f - sy) * 0.5f) * span;
                uv2[v] = new Vector2(layer, glow);
            }
            // Clockwise seen from outside (Godot's front faces); the corners run counter-clockwise, so 0-2-1, 0-3-2.
            int b = f * 4, i = f * 6;
            indices[i] = b; indices[i + 1] = b + 2; indices[i + 2] = b + 1;
            indices[i + 3] = b; indices[i + 4] = b + 3; indices[i + 5] = b + 2;
        }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.TexUV] = uvs;
        arrays[(int)Mesh.ArrayType.TexUV2] = uv2;
        arrays[(int)Mesh.ArrayType.Index] = indices;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        mesh.SurfaceSetMaterial(0, Material);
        return mesh;
    }
}
