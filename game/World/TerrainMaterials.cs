using Godot;
using Strata.Core.Meshing;

namespace Strata.Game.World;

/// <summary>Terrain texture arrays, the surface→layer lookup table and the shared terrain materials.</summary>
public sealed class TerrainMaterials
{
    public TerrainMaterials(RenderTables tables)
    {
        var albedo = GD.Load<TextureLayered>("res://assets/textures/terrain/terrain_albedo.png");
        var material = GD.Load<TextureLayered>("res://assets/textures/terrain/terrain_material.png");
        var table = BuildSurfaceTable(tables);

        Opaque = Create("res://shaders/terrain.gdshader", albedo, material, table);
        Cutout = Create("res://shaders/terrain_cutout.gdshader", albedo, material, table);
        Glass = Create("res://shaders/terrain_glass.gdshader", albedo, material, table);
        Liquid = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/liquid.gdshader") };
    }

    public ShaderMaterial Opaque { get; }
    public ShaderMaterial Cutout { get; }
    public ShaderMaterial Glass { get; }
    public ShaderMaterial Liquid { get; }

    private static ShaderMaterial Create(string shaderPath, TextureLayered albedo, TextureLayered material, Texture2D table)
    {
        var m = new ShaderMaterial { Shader = GD.Load<Shader>(shaderPath) };
        m.SetShaderParameter("albedo_array", albedo);
        m.SetShaderParameter("material_array", material);
        m.SetShaderParameter("surface_table", table);
        return m;
    }

    /// <summary>One RGBAF texel per render surface: (top layer, side layer, bottom layer, unused).</summary>
    private static Texture2D BuildSurfaceTable(RenderTables tables)
    {
        int count = System.Math.Max(1, tables.Surfaces.Count);
        var image = Image.CreateEmpty(count, 1, false, Image.Format.Rgbaf);
        for (int i = 0; i < tables.Surfaces.Count; i++)
        {
            var s = tables.Surfaces[i];
            image.SetPixel(i, 0, new Color(s.Top, s.Side, s.Bottom, 0));
        }
        return ImageTexture.CreateFromImage(image);
    }
}
