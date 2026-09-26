using System.Text.Json;
using Strata.Core.Content;
using Strata.Core.Content.Loading;

namespace Strata.Core.Presentation;

/// <summary>
/// Graphics quality presets (data/config/graphics.json): engine-neutral knobs that the game maps onto the renderer
/// (anti-aliasing, shadows, screen-space effects, volumetric fog, sky, vegetation density).
/// </summary>
public sealed class GraphicsConfig
{
    public const string Path = "data/config/graphics.json";

    public static readonly string[] Upscalers = { "none", "fsr1", "fsr2" };
    public static readonly string[] AntiAliasingModes = { "none", "fxaa", "smaa", "taa" };

    /// <summary>Preset used when the player has not chosen one.</summary>
    public string Default { get; init; } = "high";

    public Dictionary<string, GraphicsPreset> Presets { get; init; } = new();

    public static GraphicsConfig Load(IDataSource data) =>
        JsonSerializer.Deserialize<GraphicsConfig>(data.ReadText(Path), ContentJson.Options) ?? throw new JsonException($"{Path} is empty");

    public GraphicsPreset Get(string name) =>
        Presets.TryGetValue(name, out var p) ? p : throw new KeyNotFoundException($"unknown graphics preset '{name}' (known: {string.Join(", ", Presets.Keys)})");

    /// <summary>Problems with the file (unknown default, values out of range); empty when valid.</summary>
    public List<string> Validate()
    {
        var problems = new List<string>();
        if (Presets.Count == 0) problems.Add("no presets");
        if (!Presets.ContainsKey(Default)) problems.Add($"default preset '{Default}' does not exist");
        foreach (var (name, p) in Presets)
            foreach (var problem in p.Validate()) problems.Add($"{name}: {problem}");
        return problems;
    }
}

/// <summary>One quality level. Ranges are checked by <see cref="Validate"/>.</summary>
public sealed class GraphicsPreset
{
    /// <summary>3D resolution relative to the window (0.5–2).</summary>
    public float RenderScale { get; init; } = 1f;

    /// <summary>none | fsr1 | fsr2 (fsr2 at scale 1 is temporal anti-aliasing at native resolution).</summary>
    public string Upscaler { get; init; } = "none";

    /// <summary>none | fxaa | smaa | taa (ignored by fsr2, which resolves its own).</summary>
    public string AntiAliasing { get; init; } = "fxaa";

    /// <summary>Multisample count: 0, 2, 4 or 8.</summary>
    public int Msaa { get; init; }

    /// <summary>Anisotropic texture filtering: 1, 2, 4, 8 or 16.</summary>
    public int Anisotropy { get; init; } = 8;

    /// <summary>Directional shadow atlas edge (power of two, 1024–16384).</summary>
    public int ShadowAtlas { get; init; } = 4096;

    /// <summary>Sun shadow range in metres.</summary>
    public float ShadowDistance { get; init; } = 80f;

    /// <summary>Soft shadow filter: 0 hard … 5 ultra.</summary>
    public int ShadowFilter { get; init; } = 2;

    /// <summary>Apparent sun diameter in degrees; above 0 shadows harden near their casters (PCSS).</summary>
    public float SunSize { get; init; }

    /// <summary>Carried torches cast shadows.</summary>
    public bool TorchShadows { get; init; }

    public bool Ssao { get; init; } = true;

    /// <summary>0 very low … 4 ultra.</summary>
    public int SsaoQuality { get; init; } = 2;

    /// <summary>Screen-space indirect light (colour bleeding).</summary>
    public bool Ssil { get; init; }

    public int SsilQuality { get; init; } = 2;

    /// <summary>Screen-space reflections on smooth opaque surfaces.</summary>
    public bool Ssr { get; init; }

    public int SsrSteps { get; init; } = 64;

    /// <summary>Volumetric fog with light shafts.</summary>
    public bool VolumetricFog { get; init; }

    /// <summary>Froxel grid edge (32–256) and depth slices (32–256).</summary>
    public int VolumetricFogSize { get; init; } = 64;

    public int VolumetricFogDepth { get; init; } = 64;

    public bool Glow { get; init; } = true;

    /// <summary>Sky radiance cubemap edge (32–2048, power of two): ambient light and reflection detail.</summary>
    public int SkyRadiance { get; init; } = 128;

    /// <summary>Cloud light-marching steps (0 draws flat clouds).</summary>
    public int CloudSteps { get; init; } = 3;

    /// <summary>Eye adaptation between bright surface and dark caves.</summary>
    public bool AutoExposure { get; init; }

    /// <summary>Grass blade density (0 disables grass) and draw distance in metres.</summary>
    public float GrassDensity { get; init; } = 0.5f;

    public float GrassDistance { get; init; } = 30f;

    /// <summary>Mesh LOD switch threshold in pixels (lower keeps detail further away).</summary>
    public float LodThreshold { get; init; } = 1f;

    public IEnumerable<string> Validate()
    {
        if (RenderScale is < 0.5f or > 2f) yield return "renderScale must be within 0.5–2";
        if (Array.IndexOf(GraphicsConfig.Upscalers, Upscaler) < 0) yield return $"upscaler must be one of {string.Join(", ", GraphicsConfig.Upscalers)}";
        if (Array.IndexOf(GraphicsConfig.AntiAliasingModes, AntiAliasing) < 0) yield return $"antiAliasing must be one of {string.Join(", ", GraphicsConfig.AntiAliasingModes)}";
        if (Msaa is not (0 or 2 or 4 or 8)) yield return "msaa must be 0, 2, 4 or 8";
        if (Anisotropy is not (1 or 2 or 4 or 8 or 16)) yield return "anisotropy must be 1, 2, 4, 8 or 16";
        if (!PowerOfTwo(ShadowAtlas, 1024, 16384)) yield return "shadowAtlas must be a power of two within 1024–16384";
        if (ShadowDistance is < 10f or > 1000f) yield return "shadowDistance must be within 10–1000 m";
        if (ShadowFilter is < 0 or > 5) yield return "shadowFilter must be within 0–5";
        if (SunSize is < 0f or > 5f) yield return "sunSize must be within 0–5 degrees";
        if (SsaoQuality is < 0 or > 4 || SsilQuality is < 0 or > 4) yield return "ssaoQuality/ssilQuality must be within 0–4";
        if (SsrSteps is < 8 or > 512) yield return "ssrSteps must be within 8–512";
        if (VolumetricFogSize is < 32 or > 256 || VolumetricFogDepth is < 32 or > 256) yield return "volumetricFogSize/Depth must be within 32–256";
        if (!PowerOfTwo(SkyRadiance, 32, 2048)) yield return "skyRadiance must be a power of two within 32–2048";
        if (CloudSteps is < 0 or > 12) yield return "cloudSteps must be within 0–12";
        if (GrassDensity is < 0f or > 1f) yield return "grassDensity must be within 0–1";
        if (GrassDistance is < 0f or > 200f) yield return "grassDistance must be within 0–200 m";
        if (LodThreshold is < 0f or > 16f) yield return "lodThreshold must be within 0–16";
    }

    private static bool PowerOfTwo(int v, int min, int max) => v >= min && v <= max && (v & (v - 1)) == 0;
}
