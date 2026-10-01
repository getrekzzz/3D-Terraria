using System.Text.Json;
using Godot;
using Strata.Core.Meshing;

namespace Strata.Game.World;

/// <summary>
/// Terrain texture arrays, the surface→layer lookup table, the per-layer detail table and the shared terrain
/// materials. Layer metadata (detail grain per layer) comes from the texture build's layers.json.
/// </summary>
public sealed class TerrainMaterials
{
    public TerrainMaterials(RenderTables tables)
    {
        var albedo = GD.Load<TextureLayered>("res://assets/textures/terrain/terrain_albedo.png");
        var material = GD.Load<TextureLayered>("res://assets/textures/terrain/terrain_material.png");
        var detail = GD.Load<TextureLayered>("res://assets/textures/detail/detail.png");
        var table = BuildSurfaceTable(tables);
        var layers = BuildLayerTable("res://assets/textures/terrain/layers.json");

        Opaque = Create("res://shaders/terrain.gdshader", albedo, material, detail, table, layers);
        var paints = new Vector3[tables.PaintColors.Length];
        for (int i = 0; i < paints.Length; i++) paints[i] = new Vector3(tables.PaintColors[i].X, tables.PaintColors[i].Y, tables.PaintColors[i].Z);
        Opaque.SetShaderParameter("paint_colors", paints);
        Cutout = Create("res://shaders/terrain_cutout.gdshader", albedo, material, detail, table, layers);
        Glass = Create("res://shaders/terrain_glass.gdshader", albedo, material, detail, table, layers);
        Liquid = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/liquid.gdshader") };
        Lava = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/lava.gdshader") };
    }

    public ShaderMaterial Opaque { get; }

    /// <summary>Applies a graphics preset's terrain settings (how far ore glints reach).</summary>
    public void ApplyGraphics(Strata.Core.Presentation.GraphicsPreset preset) =>
        Opaque.SetShaderParameter("ore_glint_distance", preset.OreGlintDistance);
    public ShaderMaterial Cutout { get; }
    public ShaderMaterial Glass { get; }
    public ShaderMaterial Liquid { get; }

    /// <summary>Lava: opaque, its own pass (no screen copy needed).</summary>
    public ShaderMaterial Lava { get; }

    private static ShaderMaterial Create(string shaderPath, TextureLayered albedo, TextureLayered material, TextureLayered detail, Texture2D table, Texture2D layers)
    {
        var m = new ShaderMaterial { Shader = GD.Load<Shader>(shaderPath) };
        m.SetShaderParameter("albedo_array", albedo);
        m.SetShaderParameter("material_array", material);
        m.SetShaderParameter("detail_array", detail);
        m.SetShaderParameter("surface_table", table);
        m.SetShaderParameter("layer_table", layers);
        return m;
    }

    /// <summary>
    /// One RGBAF texel per texture layer: (detail layer, detail strength, ore sparkle, ore tint). The tint is the ore
    /// recipe's colour packed exactly into one float as 0xRRGGBB (glints take the vein's colour); 0 when not an ore.
    /// </summary>
    private static Texture2D BuildLayerTable(string path)
    {
        using var doc = JsonDocument.Parse(FileAccess.GetFileAsString(path));
        var entries = doc.RootElement.GetProperty("layers");
        int count = System.Math.Max(1, entries.GetArrayLength());
        var image = Image.CreateEmpty(count, 1, false, Image.Format.Rgbaf);
        foreach (var layer in entries.EnumerateArray())
        {
            float index = layer.TryGetProperty("detail", out var d) ? d.GetSingle() : 0f;
            float strength = layer.TryGetProperty("detailStrength", out var st) ? st.GetSingle() : 0f;
            string name = layer.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            float tint = layer.TryGetProperty("ore", out var ore) && ore.GetString() is { Length: 7 } hex ? System.Convert.ToInt32(hex.Substring(1), 16) : 0f;
            image.SetPixel(layer.GetProperty("index").GetInt32(), 0, new Color(index, strength, Sparkle(name), tint));
        }
        return ImageTexture.CreateFromImage(image);
    }

    private static readonly string[] Gems = { "amethyst", "topaz", "sapphire", "emerald", "ruby", "diamond" };

    // Ore layers without the _ore suffix (meteorite, hellstone's cinderstone, luminite).
    private static readonly string[] OreLayers = { "meteorite", "cinderstone", "luminite" };

    /// <summary>How strongly an ore layer glints (gems most, metals less, everything else not at all).</summary>
    private static float Sparkle(string layer) =>
        !layer.EndsWith("_ore") && System.Array.IndexOf(OreLayers, layer) < 0 ? 0f : System.Array.Exists(Gems, g => layer.StartsWith(g)) ? 1f : 0.8f;

    /// <summary>One RGBAF texel per render surface: (top layer, side layer, bottom layer, self-glow).</summary>
    private static Texture2D BuildSurfaceTable(RenderTables tables)
    {
        int count = System.Math.Max(1, tables.Surfaces.Count);
        var image = Image.CreateEmpty(count, 1, false, Image.Format.Rgbaf);
        for (int i = 0; i < tables.Surfaces.Count; i++)
        {
            var s = tables.Surfaces[i];
            image.SetPixel(i, 0, new Color(s.Top, s.Side, s.Bottom, tables.SurfaceGlow[i]));
        }
        return ImageTexture.CreateFromImage(image);
    }
}
