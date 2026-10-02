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
    public string Default { get; set; } = "high";

    public Dictionary<string, GraphicsPreset> Presets { get; set; } = new();

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
    public float RenderScale { get; set; } = 1f;

    /// <summary>Lower the 3D resolution (upscaled with FSR) while frames run slow, and raise it back when they recover.</summary>
    public bool DynamicResolution { get; set; } = true;

    /// <summary>none | fsr1 | fsr2 (fsr2 at scale 1 is temporal anti-aliasing at native resolution).</summary>
    public string Upscaler { get; set; } = "none";

    /// <summary>none | fxaa | smaa | taa (ignored by fsr2, which resolves its own).</summary>
    public string AntiAliasing { get; set; } = "fxaa";

    /// <summary>Multisample count: 0, 2, 4 or 8.</summary>
    public int Msaa { get; set; }

    /// <summary>Anisotropic texture filtering: 1, 2, 4, 8 or 16.</summary>
    public int Anisotropy { get; set; } = 8;

    /// <summary>Directional shadow atlas edge (power of two, 1024–16384).</summary>
    public int ShadowAtlas { get; set; } = 4096;

    /// <summary>Sun shadow range in metres.</summary>
    public float ShadowDistance { get; set; } = 80f;

    /// <summary>Soft shadow filter: 0 hard … 5 ultra.</summary>
    public int ShadowFilter { get; set; } = 2;

    /// <summary>Apparent sun diameter in degrees; above 0 shadows harden near their casters (PCSS).</summary>
    public float SunSize { get; set; }

    /// <summary>Sun shadow cascades: 1, 2 or 4 (more keeps near shadows sharp over a long range, at a cost).</summary>
    public int ShadowSplits { get; set; } = 4;

    /// <summary>Carried torches cast shadows.</summary>
    public bool TorchShadows { get; set; }

    /// <summary>
    /// Real (shadowless) lights for the nearest placed torches, lava and glowing creatures (0 = baked light only, which
    /// stops 7.5 m from a torch).
    /// </summary>
    public int DynamicLights { get; set; } = 8;

    public bool Ssao { get; set; } = true;

    /// <summary>0 very low … 4 ultra.</summary>
    public int SsaoQuality { get; set; } = 2;

    /// <summary>Screen-space indirect light (colour bleeding).</summary>
    public bool Ssil { get; set; }

    public int SsilQuality { get; set; } = 2;

    /// <summary>Compute SSAO / SSIL at half resolution (much cheaper, slightly softer).</summary>
    public bool SsaoHalf { get; set; } = true;

    public bool SsilHalf { get; set; } = true;

    /// <summary>Screen-space reflections on smooth opaque surfaces.</summary>
    public bool Ssr { get; set; }

    public int SsrSteps { get; set; } = 64;

    /// <summary>Volumetric fog with light shafts.</summary>
    public bool VolumetricFog { get; set; }

    /// <summary>Froxel grid edge (32–256) and depth slices (32–256).</summary>
    public int VolumetricFogSize { get; set; } = 64;

    public int VolumetricFogDepth { get; set; } = 64;

    public bool Glow { get; set; } = true;

    /// <summary>Sky radiance cubemap edge (32–2048, power of two): ambient light and reflection detail.</summary>
    public int SkyRadiance { get; set; } = 128;

    /// <summary>Cloud light-marching steps (0 draws flat clouds).</summary>
    public int CloudSteps { get; set; } = 3;

    /// <summary>
    /// Godot's metering on top of the eye adaptation every preset gets (<see cref="ExposureModel"/>): it fine-tunes
    /// brightness to the frame within lighting.json's autoMin–autoMax window.
    /// </summary>
    public bool AutoExposure { get; set; }

    /// <summary>Grass blade density (0 disables grass) and draw distance in metres.</summary>
    public float GrassDensity { get; set; } = 0.5f;

    public float GrassDistance { get; set; } = 30f;

    /// <summary>Floating combat numbers on screen at once (8–256); past it the oldest number is reused.</summary>
    public int CombatNumbers { get; set; } = 48;

    /// <summary>Chunk grass buffers built or rebuilt per frame at most (nearest first; spreads walking-in pops).</summary>
    public int GrassBuildsPerFrame { get; set; } = 2;

    /// <summary>Mesh LOD switch threshold in pixels (lower keeps detail further away).</summary>
    public float LodThreshold { get; set; } = 1f;

    /// <summary>How far the horizon (the world's surface beyond the streamed chunks) reaches, in metres; 0 = none.</summary>
    public float HorizonDistance { get; set; }

    /// <summary>Multiplies the particle counts of hit, mining and death effects (EffectKit).</summary>
    public float EffectsScale { get; set; } = 0.7f;

    /// <summary>Short-lived flash lights (explosions, sparks) alive at once; 0 disables them.</summary>
    public int FlashLights { get; set; } = 2;

    /// <summary>How far ore veins twinkle with light-catching glints, in metres (they fade over the last 30 %).</summary>
    public float OreGlintDistance { get; set; } = 24f;

    /// <summary>Hair and loose clothing (robes, coat tails) sway on characters within this distance, in metres; 0 = off.</summary>
    public float SwayDistance { get; set; } = 20f;

    /// <summary>A copy to edit into a custom preset.</summary>
    public GraphicsPreset Clone() => (GraphicsPreset)MemberwiseClone();

    public IEnumerable<string> Validate()
    {
        if (ShadowSplits is not (1 or 2 or 4)) yield return "shadowSplits must be 1, 2 or 4";
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
        if (GrassBuildsPerFrame is < 0 or > 16) yield return "grassBuildsPerFrame must be within 0–16";
        if (LodThreshold is < 0f or > 16f) yield return "lodThreshold must be within 0–16";
        if (DynamicLights is < 0 or > 64) yield return "dynamicLights must be within 0–64";
        if (EffectsScale is < 0f or > 2f) yield return "effectsScale must be within 0–2";
        if (FlashLights is < 0 or > 16) yield return "flashLights must be within 0–16";

        if (CombatNumbers is < 8 or > 256) yield return "combatNumbers must be within 8–256";
        if (HorizonDistance != 0f && HorizonDistance is < 200f or > 6000f) yield return "horizonDistance must be 0 or within 200–6000 m";
        if (OreGlintDistance is < 1f or > 60f) yield return "oreGlintDistance must be within 1–60 m";
        if (SwayDistance is < 0f or > 120f) yield return "swayDistance must be within 0–120 m";
    }

    private static bool PowerOfTwo(int v, int min, int max) => v >= min && v <= max && (v & (v - 1)) == 0;
}
