using System;
using System.Linq;
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

    /// <summary>Name of the user-tuned preset (a copy of a base preset with individual options changed).</summary>
    public const string Custom = "custom";

    public static GraphicsSettings Load(GodotDataSource data, CommandLine args, GodotLog log)
    {
        var config = GraphicsConfig.Load(data);
        var problems = config.Validate();
        if (problems.Count > 0) throw new InvalidOperationException($"{GraphicsConfig.Path}: {string.Join("; ", problems)}");
        string? wanted = args.Get("graphics");
        GraphicsPreset? custom = null;
        if (wanted == null)
        {
            var file = new ConfigFile();
            if (file.Load(SettingsFile) == Error.Ok)
            {
                wanted = (string)file.GetValue("graphics", "preset", config.Default);
                if (wanted == Custom) custom = ReadCustom(file, config.Get(config.Default), log);
            }
        }
        if (custom != null) return new GraphicsSettings(config, config.Default) { Preset = custom, Name = Custom };
        // First launch: integrated and software GPUs start on the low preset.
        if (wanted == null && config.Presets.ContainsKey("low") && RenderingServer.GetVideoAdapterType() is RenderingDevice.DeviceType.IntegratedGpu or RenderingDevice.DeviceType.Cpu)
        {
            log.Write(Strata.Core.Diagnostics.LogLevel.Info, "graphics", $"First start on '{RenderingServer.GetVideoAdapterName()}' (integrated graphics): using the low preset.");
            wanted = "low";
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
        if (remember) Save();
    }

    /// <summary>Uses a tuned copy of a preset (the settings menu's individual options) and remembers it.</summary>
    public void SelectCustom(GraphicsPreset preset, Viewport viewport)
    {
        var problems = string.Join("; ", preset.Validate());
        if (problems.Length > 0) throw new InvalidOperationException($"custom graphics preset: {problems}");
        Preset = preset;
        Name = Custom;
        Apply(viewport);
        Save();
    }

    private void Save()
    {
        var file = new ConfigFile();
        file.SetValue("graphics", "preset", Name);
        if (Name == Custom)
            foreach (var prop in typeof(GraphicsPreset).GetProperties())
            {
                var v = prop.GetValue(Preset);
                file.SetValue(Custom, prop.Name, v switch { bool b => b, int i => i, float f => f, string str => str, _ => default(Variant) });
            }
        var error = file.Save(SettingsFile);
        if (error != Error.Ok) throw new InvalidOperationException($"could not save {SettingsFile}: {error}");
    }

    private static GraphicsPreset? ReadCustom(ConfigFile file, GraphicsPreset basis, GodotLog log)
    {
        var p = basis.Clone();
        foreach (var prop in typeof(GraphicsPreset).GetProperties())
        {
            if (!prop.CanWrite || !file.HasSectionKey(Custom, prop.Name)) continue;
            var v = file.GetValue(Custom, prop.Name);
            object value = prop.PropertyType == typeof(bool) ? v.AsBool() : prop.PropertyType == typeof(int) ? v.AsInt32()
                : prop.PropertyType == typeof(float) ? v.AsSingle() : v.AsString();
            prop.SetValue(p, value);
        }
        if (!p.Validate().Any()) return p;
        log.Write(Strata.Core.Diagnostics.LogLevel.Warning, "graphics", "Custom graphics settings were invalid; using the default preset.");
        return null;
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
        RenderingServer.EnvironmentSetSsaoQuality((RenderingServer.EnvironmentSsaoQuality)p.SsaoQuality, p.SsaoHalf, 0.5f, 2, 50f, 300f);
        RenderingServer.EnvironmentSetSsilQuality((RenderingServer.EnvironmentSsilQuality)p.SsilQuality, p.SsilHalf, 0.5f, 4, 50f, 300f);
        RenderingServer.EnvironmentSetVolumetricFogVolumeSize(p.VolumetricFogSize, p.VolumetricFogDepth);
        RenderingServer.EnvironmentSetVolumetricFogFilterActive(true);
        RenderingServer.EnvironmentGlowSetUseBicubicUpscale(true);
        Changed?.Invoke(p);
    }
}
