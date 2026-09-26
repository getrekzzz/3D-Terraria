using System;
using Godot;
using Strata.Core.Presentation;
using Strata.Game.Bootstrap;

namespace Strata.Game.Rendering;

/// <summary>
/// Applies graphics presets from data/config/graphics.json to the renderer: viewport anti-aliasing and scaling,
/// global shadow and screen-space effect quality. Scene parts (environment, lights, grass) follow
/// <see cref="Changed"/>. The chosen preset is remembered in user://graphics.cfg; --graphics=NAME overrides it.
/// </summary>
public sealed class GraphicsSettings
{
    private const string SettingsFile = "user://graphics.cfg";

    private GraphicsSettings(GraphicsConfig config, string name)
    {
        Config = config;
        Name = name;
        Preset = config.Get(name);
    }

    public GraphicsConfig Config { get; }

    public string Name { get; private set; }

    public GraphicsPreset Preset { get; private set; }

    public event Action<GraphicsPreset>? Changed;

    public static GraphicsSettings Load(GodotDataSource data, CommandLine args, GodotLog log)
    {
        var config = GraphicsConfig.Load(data);
        var problems = config.Validate();
        if (problems.Count > 0) throw new InvalidOperationException($"{GraphicsConfig.Path}: {string.Join("; ", problems)}");
        string? wanted = args.Get("graphics");
        if (wanted == null)
        {
            var file = new ConfigFile();
            if (file.Load(SettingsFile) == Error.Ok) wanted = (string)file.GetValue("graphics", "preset", config.Default);
        }
        if (wanted == null || !config.Presets.ContainsKey(wanted))
        {
            if (wanted != null) log.Write(Strata.Core.Diagnostics.LogLevel.Warning, "graphics", $"Unknown graphics preset '{wanted}', using '{config.Default}'.");
            wanted = config.Default;
        }
        return new GraphicsSettings(config, wanted);
    }

    /// <summary>Switches preset; <paramref name="remember"/> stores it for the next start.</summary>
    public void Select(string name, Viewport viewport, bool remember)
    {
        Preset = Config.Get(name);
        Name = name;
        Apply(viewport);
        if (remember)
        {
            var file = new ConfigFile();
            file.SetValue("graphics", "preset", name);
            var error = file.Save(SettingsFile);
            if (error != Error.Ok) throw new InvalidOperationException($"could not save {SettingsFile}: {error}");
        }
    }

    /// <summary>Pushes the current preset to the viewport and renderer and notifies listeners.</summary>
    public void Apply(Viewport viewport)
    {
        var p = Preset;
        viewport.Scaling3DMode = p.Upscaler switch
        {
            "fsr1" => Viewport.Scaling3DModeEnum.Fsr,
            "fsr2" => Viewport.Scaling3DModeEnum.Fsr2,
            _ => Viewport.Scaling3DModeEnum.Bilinear,
        };
        viewport.Scaling3DScale = p.RenderScale;
        viewport.ScreenSpaceAA = p.AntiAliasing switch
        {
            "fxaa" => Viewport.ScreenSpaceAAEnum.Fxaa,
            "smaa" => Viewport.ScreenSpaceAAEnum.Smaa,
            _ => Viewport.ScreenSpaceAAEnum.Disabled,
        };
        viewport.UseTaa = p.AntiAliasing == "taa" && p.Upscaler != "fsr2";
        viewport.Msaa3D = p.Msaa switch
        {
            2 => Viewport.Msaa.Msaa2X,
            4 => Viewport.Msaa.Msaa4X,
            8 => Viewport.Msaa.Msaa8X,
            _ => Viewport.Msaa.Disabled,
        };
        viewport.AnisotropicFilteringLevel = (Viewport.AnisotropicFiltering)Mathf.RoundToInt(Mathf.Log(p.Anisotropy) / Mathf.Log(2f));
        viewport.MeshLodThreshold = p.LodThreshold;
        viewport.UseDebanding = true;

        RenderingServer.DirectionalShadowAtlasSetSize(p.ShadowAtlas, true);
        RenderingServer.DirectionalSoftShadowFilterSetQuality((RenderingServer.ShadowQuality)p.ShadowFilter);
        RenderingServer.PositionalSoftShadowFilterSetQuality((RenderingServer.ShadowQuality)p.ShadowFilter);
        RenderingServer.EnvironmentSetSsaoQuality((RenderingServer.EnvironmentSsaoQuality)p.SsaoQuality, p.SsaoQuality < 2, 0.5f, 2, 50f, 300f);
        RenderingServer.EnvironmentSetSsilQuality((RenderingServer.EnvironmentSsilQuality)p.SsilQuality, p.SsilQuality < 2, 0.5f, 4, 50f, 300f);
        RenderingServer.EnvironmentSetVolumetricFogVolumeSize(p.VolumetricFogSize, p.VolumetricFogDepth);
        RenderingServer.EnvironmentSetVolumetricFogFilterActive(true);
        RenderingServer.EnvironmentGlowSetUseBicubicUpscale(true);
        Changed?.Invoke(p);
    }
}
