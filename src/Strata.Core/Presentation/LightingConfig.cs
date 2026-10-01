using System.Text.Json;
using Strata.Core.Content;
using Strata.Core.Content.Loading;

namespace Strata.Core.Presentation;

/// <summary>
/// Light tuning shared by every graphics preset (data/config/lighting.json): eye adaptation, glow, the near fog cap,
/// screen-space strengths, torch lights and the baked block-light response. Every value lives in the file (all
/// members are required), so calibration from screenshots never touches code.
/// </summary>
public sealed class LightingConfig
{
    public const string Path = "data/config/lighting.json";

    public required ExposureSettings Exposure { get; set; }
    public required GlowSettings Glow { get; set; }
    public required FogSettings Fog { get; set; }
    public required ScreenSpaceSettings ScreenSpace { get; set; }
    public required TorchSettings Torch { get; set; }
    public required BlockLightSettings BlockLight { get; set; }

    public static LightingConfig Load(IDataSource data) =>
        JsonSerializer.Deserialize<LightingConfig>(data.ReadText(Path), ContentJson.Options) ?? throw new JsonException($"{Path} is empty");

    /// <summary>Problems with the file (values out of range, a section written as null); empty when valid.</summary>
    public List<string> Validate()
    {
        var problems = new List<string>();
        // `required` only demands the key: "exposure": null still parses, so a null section is reported, not thrown.
        Add(problems, "exposure", Exposure?.Validate());
        Add(problems, "glow", Glow?.Validate());
        Add(problems, "fog", Fog?.Validate());
        Add(problems, "screenSpace", ScreenSpace?.Validate());
        Add(problems, "torch", Torch?.Validate());
        Add(problems, "blockLight", BlockLight?.Validate());
        return problems;
    }

    private static void Add(List<string> problems, string section, IEnumerable<string>? found)
    {
        if (found is null) problems.Add($"{section} must be an object, not null");
        else foreach (var problem in found) problems.Add($"{section}.{problem}");
    }
}

/// <summary>
/// Eye adaptation (see <see cref="ExposureModel"/>): the exposure multiplier the eye wants in a sealed cave, at night
/// and in open daylight (times the biome's <see cref="Content.Defs.BiomeDef.ExposureBias"/>), how fast it gets there,
/// and the window Godot's luminance-based auto exposure may move within on presets that enable it.
/// </summary>
public sealed class ExposureSettings
{
    /// <summary>Multiplier in a cave sealed from the sky (1–3).</summary>
    public required float CaveBoost { get; set; }

    /// <summary>Multiplier under the open night sky (0.5 up to <see cref="CaveBoost"/>).</summary>
    public required float NightBoost { get; set; }

    /// <summary>Exponential rate (per second) while the eye opens up (entering a cave).</summary>
    public required float BrightenPerSecond { get; set; }

    /// <summary>Exponential rate (per second) while the eye closes down (leaving a cave); at least the brighten rate.</summary>
    public required float DarkenPerSecond { get; set; }

    /// <summary>Godot auto exposure scale: the mid-grey luminance the metering aims for.</summary>
    public required float AutoKey { get; set; }

    /// <summary>Lowest and highest multiplier Godot's auto exposure may apply (0 &lt; min ≤ 1 ≤ max ≤ 4).</summary>
    public required float AutoMin { get; set; }

    public required float AutoMax { get; set; }

    /// <summary>Godot auto exposure adaptation speed.</summary>
    public required float AutoSpeed { get; set; }

    public IEnumerable<string> Validate()
    {
        if (CaveBoost is < 1f or > 3f) yield return "caveBoost must be within 1–3";
        if (NightBoost < 0.5f || NightBoost > CaveBoost) yield return "nightBoost must be within 0.5 and caveBoost (stepping out of a cave at night must not brighten)";
        if (BrightenPerSecond <= 0f || BrightenPerSecond > 50f) yield return "brightenPerSecond must be within 0–50 (above 0)";
        if (DarkenPerSecond <= 0f || DarkenPerSecond > 50f) yield return "darkenPerSecond must be within 0–50 (above 0)";
        if (DarkenPerSecond < BrightenPerSecond) yield return "darkenPerSecond must be at least brightenPerSecond (leaving a cave must never flash)";
        if (AutoKey <= 0f || AutoKey > 2f) yield return "autoKey must be within 0–2 (above 0)";
        if (AutoMin <= 0f || AutoMin > 1f) yield return "autoMin must be within 0–1 (above 0)";
        if (AutoMax is < 1f or > 4f) yield return "autoMax must be within 1–4";
        if (AutoSpeed <= 0f || AutoSpeed > 64f) yield return "autoSpeed must be within 0–64 (above 0)";
    }
}

/// <summary>Bloom of HDR light above a threshold (Godot environment glow, normalized level weights).</summary>
public sealed class GlowSettings
{
    public const int LevelCount = 7;

    /// <summary>HDR luminance where glow starts (after exposure).</summary>
    public required float Threshold { get; set; }

    /// <summary>Godot glow HDR scale: how softly light above the threshold ramps into glow.</summary>
    public required float Softness { get; set; }

    public required float Intensity { get; set; }

    /// <summary>Brightest luminance that feeds the glow (stops the sun's specular from flooding the screen).</summary>
    public required float LuminanceCap { get; set; }

    /// <summary>Weight of each of the 7 blur levels (fine to wide); normalized by the renderer.</summary>
    public required float[] Levels { get; set; }

    public IEnumerable<string> Validate()
    {
        if (Threshold is < 0f or > 4f) yield return "threshold must be within 0–4";
        if (Softness is < 0f or > 4f) yield return "softness must be within 0–4";
        if (Intensity is < 0f or > 8f) yield return "intensity must be within 0–8";
        if (LuminanceCap <= Threshold || LuminanceCap > 256f) yield return "luminanceCap must be above threshold and at most 256";
        if (Levels is null || Levels.Length != LevelCount) yield return $"levels must hold {LevelCount} values";
        else if (Array.Exists(Levels, l => l < 0f) || Levels.Sum() <= 0f) yield return "levels must be non-negative with a positive sum";
    }
}

/// <summary>Distance fog limits that hold on every preset, and how fast the close cave gloom follows the camera.</summary>
public sealed class FogSettings
{
    /// <summary>Fog starts no further than this (metres, divided by the biome's fog density), so distant land fades.</summary>
    public required float NearCap { get; set; }

    /// <summary>Exponential rate (per second) at which the cave gloom closes in when the camera loses the sky.</summary>
    public required float ClosePerSecond { get; set; }

    /// <summary>Exponential rate (per second) at which the gloom clears under the open sky; at least the close rate.</summary>
    public required float ClearPerSecond { get; set; }

    public IEnumerable<string> Validate()
    {
        if (NearCap is < 1f or > 1000f) yield return "nearCap must be within 1–1000 m";
        if (ClosePerSecond <= 0f || ClosePerSecond > 50f) yield return "closePerSecond must be within 0–50 (above 0)";
        if (ClearPerSecond <= 0f || ClearPerSecond > 50f) yield return "clearPerSecond must be within 0–50 (above 0)";
        if (ClearPerSecond < ClosePerSecond) yield return "clearPerSecond must be at least closePerSecond (leaving a cave must not stay hazy)";
    }
}

/// <summary>Strengths of the screen-space effects on the presets that enable them.</summary>
public sealed class ScreenSpaceSettings
{
    /// <summary>Screen-space indirect light (colour bleeding) strength.</summary>
    public required float SsilIntensity { get; set; }

    /// <summary>Grey albedo of the volumetric fog (how much light it scatters back).</summary>
    public required float VolumetricAlbedo { get; set; }

    public IEnumerable<string> Validate()
    {
        if (SsilIntensity is < 0f or > 16f) yield return "ssilIntensity must be within 0–16";
        if (VolumetricAlbedo is < 0f or > 1f) yield return "volumetricAlbedo must be within 0–1";
    }
}

/// <summary>
/// Real lights for torches: placed ones (the nearest few get a pooled light) and the one carried in the hand. Energy
/// and range scale with the light's peak channel (0–1 of the baked light level); attenuation is Godot's omni falloff.
/// </summary>
public sealed class TorchSettings
{
    public required float PlacedEnergy { get; set; }
    public required float PlacedRangeBase { get; set; }
    public required float PlacedRangePerPeak { get; set; }
    public required float PlacedAttenuation { get; set; }
    public required float HeldEnergy { get; set; }
    public required float HeldRangeBase { get; set; }
    public required float HeldRangePerPeak { get; set; }
    public required float HeldAttenuation { get; set; }

    public IEnumerable<string> Validate()
    {
        foreach (var p in Light("placed", PlacedEnergy, PlacedRangeBase, PlacedRangePerPeak, PlacedAttenuation)) yield return p;
        foreach (var p in Light("held", HeldEnergy, HeldRangeBase, HeldRangePerPeak, HeldAttenuation)) yield return p;
    }

    private static IEnumerable<string> Light(string kind, float energy, float rangeBase, float rangePerPeak, float attenuation)
    {
        if (energy <= 0f || energy > 16f) yield return $"{kind}Energy must be within 0–16 (above 0)";
        if (rangeBase < 0f) yield return $"{kind}RangeBase must be ≥ 0";
        if (rangePerPeak <= 0f) yield return $"{kind}RangePerPeak must be above 0";
        else if (rangeBase + rangePerPeak > 64f) yield return $"{kind}RangeBase + {kind}RangePerPeak must be at most 64 m";
        if (attenuation is < 0.2f or > 2f) yield return $"{kind}Attenuation must be within 0.2–2";
    }
}

/// <summary>
/// Response of baked block light (torches, lava, glowing ores) in the voxel shaders: level * mix(linear, 1, level),
/// times gain; in full daylight it is damped by daylitSuppression (the eye is adapted to the sun).
/// </summary>
public sealed class BlockLightSettings
{
    /// <summary>Linear share of the curve (0 = level², 1 = linear): higher keeps a long soft tail.</summary>
    public required float Linear { get; set; }

    public required float Gain { get; set; }

    /// <summary>Share of block light removed on surfaces in full daylight (0–1).</summary>
    public required float DaylitSuppression { get; set; }

    public IEnumerable<string> Validate()
    {
        if (Linear is < 0f or > 1f) yield return "linear must be within 0–1";
        if (Gain <= 0f || Gain > 4f) yield return "gain must be within 0–4 (above 0)";
        if (DaylitSuppression is < 0f or > 1f) yield return "daylitSuppression must be within 0–1";
    }
}
