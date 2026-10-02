using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace Strata.Game.Rendering;

/// <summary>
/// Loads generated GLB models and gives every surface the shared prop shader. GLB material names encode the
/// texture layer and flags (<c>prop:planks</c>, <c>prop:plain:glow</c>, translucent <c>prop:gel:clear</c>, tinted metal
/// <c>prop:iron:metal</c>); layers come from
/// res://assets/textures/props/layers.json. Tree crowns use alpha-tested leaf cards (<c>card:oak_sprig</c>); people use
/// <c>char:&lt;kind&gt;</c> slots (see <see cref="CharacterMaterials"/>).
/// Meshes and materials are cached.
/// </summary>
public sealed partial class PropLibrary
{
    private const string Folder = "res://assets/textures/props/";
    private static readonly Shader PropShader = GD.Load<Shader>("res://shaders/prop.gdshader");
    private static readonly Shader PropClearShader = GD.Load<Shader>("res://shaders/prop_clear.gdshader");
    private static readonly Shader CardShader = GD.Load<Shader>("res://shaders/foliage_card.gdshader");

    // Brightness tinted metal slots are normalised to (sRGB): the tint is then the colour seen on a lit, average texel.
    private const float MetalTarget = 0.85f;
    private readonly Dictionary<string, (int Index, float Sway)> _layers = new();
    private readonly Dictionary<string, float> _means = new();
    private readonly Dictionary<(string Slot, bool Custom), ShaderMaterial> _materials = new();
    private readonly Dictionary<(string Path, bool Custom), Mesh?> _meshes = new();
    private readonly TextureLayered _albedo;
    private readonly TextureLayered _material;
    private readonly CharacterMaterials _characters = new();

    public PropLibrary()
    {
        _albedo = GD.Load<TextureLayered>(Folder + "props_albedo.png");
        _material = GD.Load<TextureLayered>(Folder + "props_material.png");
        using var doc = JsonDocument.Parse(FileAccess.GetFileAsString(Folder + "layers.json"));
        foreach (var layer in doc.RootElement.GetProperty("layers").EnumerateArray())
        {
            float sway = layer.TryGetProperty("sway", out var s) ? s.GetSingle() : 0f;
            var name = layer.GetProperty("name").GetString()!;
            _layers[name] = (layer.GetProperty("index").GetInt32(), sway);
            if (layer.TryGetProperty("mean", out var mean)) _means[name] = mean.GetSingle();
        }
    }

    /// <summary>Errors found while loading (missing files, unknown layers); surfaced by the smoke test.</summary>
    public List<string> Problems { get; } = new();

    /// <summary>Records a problem once (a missing model asked for every frame is reported a single time).</summary>
    public void Report(string problem)
    {
        if (_reported.Add(problem)) Problems.Add(problem);
    }

    private readonly HashSet<string> _reported = new();

    /// <summary>
    /// The mesh of a model with prop materials, or null when the file is missing (reported in <see cref="Problems"/>).
    /// Loads synchronously (finishing a pending threaded load): for the player's own gear, which must show at once.
    /// <paramref name="multimesh"/> selects materials that read light from MultiMesh custom data.
    /// </summary>
    public Mesh? Model(string projectPath, bool multimesh)
    {
        var key = (projectPath, multimesh);
        if (_meshes.TryGetValue(key, out var cached)) return cached;
        var path = "res://" + projectPath;
        return Build(key, AssetCache.LoadNow<PackedScene>(path), path);
    }

    /// <summary>
    /// Like <see cref="Model"/>, but a model not loaded yet is requested on a loader thread and reported Pending (nothing
    /// is cached or reported); ask again on a later frame. Sockets and bounds are available once it is Ready.
    /// </summary>
    public AssetState TryModel(string projectPath, bool multimesh, out Mesh? mesh)
    {
        var key = (projectPath, multimesh);
        if (_meshes.TryGetValue(key, out mesh)) return mesh != null ? AssetState.Ready : AssetState.Missing;
        var path = "res://" + projectPath;
        if (AssetCache.TryGet<PackedScene>(path, out var scene) == AssetState.Pending) return AssetState.Pending;
        mesh = Build(key, scene, path);
        return mesh != null ? AssetState.Ready : AssetState.Missing;
    }

    /// <summary>True while a model is loading on a loader thread.</summary>
    public static bool IsPending(string projectPath) => AssetCache.IsPending("res://" + projectPath);

    private Mesh? Build((string Path, bool Custom) key, PackedScene? packed, string path)
    {
        Mesh? result = null;
        if (packed == null) Report($"missing model {path}");
        else
        {
            using var profile = Strata.Core.Diagnostics.Metrics.Frame.Time("assets.instantiate");
            var scene = packed.Instantiate();
            var source = FindMesh(scene);
            if (source == null) Report($"model {path} contains no mesh");
            else
            {
                result = Retarget(source.Mesh, key.Custom, path);
                CollectSockets(key.Path, source);
            }
            scene.Free();
        }
        _meshes[key] = result;
        return result;
    }

    public ShaderMaterial Material(string slot, bool multimesh)
    {
        var key = (slot, multimesh);
        if (_materials.TryGetValue(key, out var m)) return m;
        if (slot.StartsWith("char:") && _characters.Get(slot, _albedo, _material, _layers, Problems) is { } character)
            return _materials[key] = character;
        bool card = slot.StartsWith("card:");
        var parts = slot.StartsWith("prop:") || card ? slot.Substring(5).Split(':') : new[] { "missing" };
        if (!_layers.TryGetValue(parts[0], out var layer))
        {
            Problems.Add($"unknown prop layer in material '{slot}'");
            layer = _layers.TryGetValue("missing", out var missing) ? missing : (0, 0f);
        }
        if (card)
        {
            m = new ShaderMaterial { Shader = CardShader };
            m.SetShaderParameter("albedo_array", _albedo);
            m.SetShaderParameter("layer", (float)layer.Index);
            m.SetShaderParameter("sway", layer.Sway);
            m.SetShaderParameter("light_from_custom", multimesh);
            return _materials[key] = m;
        }
        m = new ShaderMaterial { Shader = System.Array.IndexOf(parts, "clear") > 0 ? PropClearShader : PropShader };
        m.SetShaderParameter("albedo_array", _albedo);
        m.SetShaderParameter("material_array", _material);
        m.SetShaderParameter("layer", (float)layer.Index);
        m.SetShaderParameter("sway", layer.Sway);
        m.SetShaderParameter("foliage", layer.Sway > 0f ? 0.6f : 0f); // swaying layers are leaves: light passes through
        m.SetShaderParameter("glow", System.Array.IndexOf(parts, "glow") > 0 ? 1.1f : 0f);
        if (System.Array.IndexOf(parts, "metal") > 0 && _means.TryGetValue(parts[0], out var mean) && mean > 0.01f)
            m.SetShaderParameter("neutral_gain", Mathf.Pow(MetalTarget / mean, 2.2f));
        m.SetShaderParameter("light_from_custom", multimesh);
        _materials[key] = m;
        return m;
    }

    private Mesh Retarget(Mesh source, bool multimesh, string path)
    {
        var mesh = (Mesh)source.Duplicate();
        for (int i = 0; i < mesh.GetSurfaceCount(); i++)
        {
            var name = source.SurfaceGetMaterial(i)?.ResourceName ?? "";
            if (name.Length == 0) Problems.Add($"{path} surface {i} has no material name");
            ((ArrayMesh)mesh).SurfaceSetMaterial(i, Material(name.Length == 0 ? "prop:missing" : name, multimesh));
        }
        return mesh;
    }

    private static MeshInstance3D? FindMesh(Node node)
    {
        if (node is MeshInstance3D mi && mi.Mesh != null) return mi;
        foreach (var child in node.GetChildren())
            if (FindMesh(child) is { } m) return m;
        return null;
    }
}
