using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace Strata.Game.Rendering;

/// <summary>
/// Materials for generated people: GLB slots named <c>char:&lt;kind&gt;</c> (skin, eye, hair, nail, cloth, leather,
/// metal, bone, glow) get the character shader with that kind's texture layer and surface response; <c>char:glass</c>
/// (helmet domes, visors) gets the see-through character glass shader. The slot list matches tools/blender/bgen/human
/// (SLOTS).
/// </summary>
public sealed class CharacterMaterials
{
    private static readonly Shader Shader = GD.Load<Shader>("res://shaders/character.gdshader");
    private static readonly Shader GlassShader = GD.Load<Shader>("res://shaders/character_glass.gdshader");

    /// <summary>kind, props layer, tile (m), detail layer, detail strength, detail tile (m), roughness, metallic, sss, sheen.</summary>
    private static readonly Dictionary<string, (int Kind, string Layer, float Tile, string Detail, float DetailStrength, float DetailTile, float Rough, float Metal, float Sss, float Sheen)> Kinds = new()
    {
        ["skin"] = (0, "plain", 0.1f, "porous", 0.35f, 0.018f, 0.52f, 0f, 0.2f, 0f),
        ["eye"] = (1, "plain", 0.1f, "smooth", 0f, 0.02f, 0.03f, 0f, 0f, 0f),
        ["hair"] = (2, "plain", 0.1f, "grass", 0f, 0.02f, 0.62f, 0f, 0f, 0f),
        ["nail"] = (3, "plain", 0.1f, "smooth", 0f, 0.02f, 0.22f, 0f, 0f, 0f),
        ["cloth"] = (4, "cloth", 0.06f, "porous", 0f, 0.02f, 0.9f, 0f, 0f, 1f),
        ["leather"] = (5, "leather", 0.12f, "porous", 0f, 0.02f, 0.5f, 0f, 0f, 0f),
        ["metal"] = (6, "iron", 0.2f, "smooth", 0f, 0.02f, 0.3f, 1f, 0f, 0f),
        ["bone"] = (7, "stone", 0.08f, "porous", 0f, 0.02f, 0.65f, 0f, 0f, 0f),
        ["glow"] = (8, "plain", 0.1f, "smooth", 0f, 0.02f, 0.5f, 0f, 0f, 0f),
        ["glass"] = (9, "plain", 0.1f, "smooth", 0f, 0.02f, 0.04f, 0f, 0f, 0f),
    };

    private readonly Dictionary<string, ShaderMaterial> _cache = new();
    private readonly Dictionary<string, int> _detail = new();
    private readonly TextureLayered _detailArray;

    public CharacterMaterials()
    {
        _detailArray = GD.Load<TextureLayered>("res://assets/textures/detail/detail.png");
        using var doc = JsonDocument.Parse(FileAccess.GetFileAsString("res://assets/textures/detail/layers.json"));
        int i = 0;
        foreach (var name in doc.RootElement.GetProperty("layers").EnumerateArray()) _detail[name.GetString()!] = i++;
    }

    /// <summary>The material for a <c>char:</c> slot, or null (with a problem recorded) for an unknown kind.</summary>
    public ShaderMaterial? Get(string slot, TextureLayered albedo, TextureLayered material, IReadOnlyDictionary<string, (int Index, float Sway)> layers, List<string> problems)
    {
        if (_cache.TryGetValue(slot, out var cached)) return cached;
        var kindName = slot.Substring("char:".Length);
        if (!Kinds.TryGetValue(kindName, out var k))
        {
            problems.Add($"unknown character material '{slot}'");
            return null;
        }
        if (!layers.TryGetValue(k.Layer, out var layer)) problems.Add($"character material '{slot}' needs prop layer '{k.Layer}'");
        if (!_detail.TryGetValue(k.Detail, out int detail)) problems.Add($"character material '{slot}' needs detail layer '{k.Detail}'");
        var m = new ShaderMaterial { Shader = kindName == "glass" ? GlassShader : Shader };
        m.SetShaderParameter("kind", k.Kind);
        m.SetShaderParameter("albedo_array", albedo);
        m.SetShaderParameter("material_array", material);
        m.SetShaderParameter("detail_array", _detailArray);
        m.SetShaderParameter("layer", (float)layer.Index);
        m.SetShaderParameter("tile", k.Tile);
        m.SetShaderParameter("detail_layer", (float)detail);
        m.SetShaderParameter("detail_strength", k.DetailStrength);
        m.SetShaderParameter("detail_tile", k.DetailTile);
        m.SetShaderParameter("roughness_base", k.Rough);
        m.SetShaderParameter("metallic_base", k.Metal);
        m.SetShaderParameter("sss", k.Sss);
        m.SetShaderParameter("sheen", k.Sheen);
        _cache[slot] = m;
        return m;
    }
}
