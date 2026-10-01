using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Strata.Core.Presentation;
using Strata.Game.Rendering;
using Strata.Game.Settings;

namespace Strata.Game.Menu;

public sealed partial class SettingsScreen
{
    private static readonly string[] Presets = { "low", "medium", "high", "ultra" };
    private static readonly Vector2I[] Resolutions = { new(1280, 720), new(1600, 900), new(1920, 1080), new(2560, 1440), new(3840, 2160) };
    private readonly List<Action> _refreshVideo = new();
    private OptionButton? _preset;

    private GraphicsSettings Graphics => _services.Graphics;

    private void Video(VBoxContainer box)
    {
        box.AddChild(Header("Display"));
        box.AddChild(MenuKit.Row("Window mode", MenuKit.Choice(new[] { "Windowed", "Fullscreen", "Borderless fullscreen" },
            Array.IndexOf(DisplaySettings.WindowModes, S.WindowMode), i => { S.WindowMode = DisplaySettings.WindowModes[i]; DisplaySettings.Apply(S); })));
        var size = DisplayServer.WindowGetSize();
        box.AddChild(MenuKit.Row("Window size", MenuKit.Choice(Resolutions.Select(r => $"{r.X} × {r.Y}").ToArray(),
            Math.Max(0, Array.FindIndex(Resolutions, r => r == size)), i => DisplayServer.WindowSetSize(Resolutions[i]))));
        box.AddChild(MenuKit.Row("VSync", MenuKit.Toggle(S.VSync, v => { S.VSync = v; DisplaySettings.Apply(S); })));
        box.AddChild(MenuKit.Row("Frame limit", MenuKit.Choice(new[] { "Unlimited", "30", "60", "120", "144", "240" },
            Math.Max(0, Array.IndexOf(new[] { 0, 30, 60, 120, 144, 240 }, S.MaxFps)), i => { S.MaxFps = new[] { 0, 30, 60, 120, 144, 240 }[i]; DisplaySettings.Apply(S); })));
        box.AddChild(MenuKit.Row("Brightness", MenuKit.Slider(0.6, 1.6, 0.05, S.Brightness, v => { S.Brightness = (float)v; Changed(); }, v => $"{v * 100:0}%")));
        box.AddChild(MenuKit.Row("View distance", MenuKit.Slider(3, 10, 1, S.ViewDistance, v => { S.ViewDistance = (int)v; Changed(); }, v => $"{v:0} chunks ({v * 16:0} m)"),
            "How far the detailed world streams around you. Lower is faster. Applies when a world is (re)loaded."));

        box.AddChild(Header("Graphics quality"));
        _preset = MenuKit.Choice(new[] { "Low", "Medium", "High", "Ultra", "Custom" }, PresetIndex(), i =>
        {
            if (i < Presets.Length) Graphics.Select(Presets[i], _viewport, remember: true);
            RefreshVideo();
        });
        box.AddChild(MenuKit.Row("Preset", _preset, "Medium suits most PCs. High and Ultra add screen-space GI, reflections and volumetric fog."));
        Option(box, "Render scale", p => p.RenderScale, (p, v) => p.RenderScale = (float)v, 0.5, 1.5, 0.05, v => $"{v * 100:0}%",
            "Below 100% renders fewer pixels (faster) and upscales.");
        Pick(box, "Anti-aliasing", new[] { "none", "fxaa", "smaa", "taa" }, new[] { "Off", "FXAA", "SMAA", "TAA" }, p => p.AntiAliasing, (p, v) => p.AntiAliasing = v);
        Pick(box, "Upscaler", new[] { "none", "fsr1", "fsr2" }, new[] { "Bilinear", "FSR 1", "FSR 2" }, p => p.Upscaler, (p, v) => p.Upscaler = v);
        Option(box, "Shadow distance", p => p.ShadowDistance, (p, v) => p.ShadowDistance = (float)v, 20, 200, 5, v => $"{v:0} m");
        Pick(box, "Shadow quality", new[] { "2048", "4096", "8192" }, new[] { "Low", "Medium", "High" },
            p => p.ShadowAtlas.ToString(), (p, v) => p.ShadowAtlas = int.Parse(v));
        Pick(box, "Shadow cascades", new[] { "1", "2", "4" }, new[] { "1", "2", "4" }, p => p.ShadowSplits.ToString(), (p, v) => p.ShadowSplits = int.Parse(v));
        Option(box, "Shadow softness", p => p.ShadowFilter, (p, v) => p.ShadowFilter = (int)v, 0, 5, 1, v => $"{v:0}");
        Flag(box, "Dynamic resolution", p => p.DynamicResolution, (p, v) => p.DynamicResolution = v, "Lowers the 3D resolution while the frame rate drops, keeping the game smooth.");
        Flag(box, "Torch shadows", p => p.TorchShadows, (p, v) => p.TorchShadows = v);
        Option(box, "Nearby lights", p => p.DynamicLights, (p, v) => p.DynamicLights = (int)v, 0, 32, 1, v => v < 1 ? "Off" : $"{v:0}",
            "Real lights for the nearest torches, lava and glowing creatures (on top of their baked glow). Fewer is faster.");
        Flag(box, "Ambient occlusion (SSAO)", p => p.Ssao, (p, v) => p.Ssao = v);
        Flag(box, "Indirect light (SSIL)", p => p.Ssil, (p, v) => p.Ssil = v, "Expensive.");
        Flag(box, "Half-resolution SSAO/SSIL", p => p.SsaoHalf && p.SsilHalf, (p, v) => p.SsaoHalf = p.SsilHalf = v, "Much cheaper, slightly softer.");
        Flag(box, "Reflections (SSR)", p => p.Ssr, (p, v) => p.Ssr = v, "Expensive.");
        Flag(box, "Volumetric fog", p => p.VolumetricFog, (p, v) => p.VolumetricFog = v, "Light shafts. Expensive.");
        Flag(box, "Bloom", p => p.Glow, (p, v) => p.Glow = v);
        Flag(box, "Auto exposure", p => p.AutoExposure, (p, v) => p.AutoExposure = v,
            "Fine-tunes brightness to what is on screen. Eyes adjust to caves and bright sand on every setting.");
        Option(box, "Grass density", p => p.GrassDensity, (p, v) => p.GrassDensity = (float)v, 0, 1, 0.05, v => $"{v * 100:0}%");
        Option(box, "Grass distance", p => p.GrassDistance, (p, v) => p.GrassDistance = (float)v, 0, 80, 1, v => $"{v:0} m");
        Option(box, "Cloud detail", p => p.CloudSteps, (p, v) => p.CloudSteps = (int)v, 0, 8, 1, v => $"{v:0}");
        Option(box, "Horizon distance", p => p.HorizonDistance, (p, v) => p.HorizonDistance = v < 200 ? 0 : (float)v, 0, 3000, 100, v => v < 200 ? "Off" : $"{v:0} m",
            "The distant landscape beyond the streamed world.");
    }

    private int PresetIndex()
    {
        int i = Array.IndexOf(Presets, Graphics.Name);
        return i < 0 ? 4 : i;
    }

    private void RefreshVideo()
    {
        if (_preset != null) _preset.Selected = PresetIndex();
        foreach (var refresh in _refreshVideo) refresh();
    }

    /// <summary>Changes one option: the current preset becomes a custom copy.</summary>
    private void Customize(Action<GraphicsPreset> change)
    {
        var p = Graphics.Preset.Clone();
        change(p);
        try
        {
            Graphics.SelectCustom(p, _viewport);
        }
        catch (InvalidOperationException ex)
        {
            GD.PushWarning(ex.Message);
        }
        if (_preset != null) _preset.Selected = PresetIndex();
    }

    private void Option(VBoxContainer box, string label, Func<GraphicsPreset, float> get, Action<GraphicsPreset, double> set,
        double min, double max, double step, Func<double, string> format, string? tip = null)
    {
        var slider = MenuKit.Slider(min, max, step, get(Graphics.Preset), v => { if (!_refreshing) Customize(p => set(p, v)); }, format);
        var s = (HSlider)slider.GetChild(0);
        _refreshVideo.Add(() => { _refreshing = true; s.Value = get(Graphics.Preset); _refreshing = false; });
        box.AddChild(MenuKit.Row(label, slider, tip));
    }

    private void Flag(VBoxContainer box, string label, Func<GraphicsPreset, bool> get, Action<GraphicsPreset, bool> set, string? tip = null)
    {
        var toggle = MenuKit.Toggle(get(Graphics.Preset), v => { if (!_refreshing) Customize(p => set(p, v)); });
        _refreshVideo.Add(() => { _refreshing = true; toggle.ButtonPressed = get(Graphics.Preset); _refreshing = false; });
        box.AddChild(MenuKit.Row(label, toggle, tip));
    }

    private void Pick(VBoxContainer box, string label, string[] values, string[] names, Func<GraphicsPreset, string> get, Action<GraphicsPreset, string> set)
    {
        var choice = MenuKit.Choice(names, Math.Max(0, Array.IndexOf(values, get(Graphics.Preset))), i => { if (!_refreshing) Customize(p => set(p, values[i])); });
        _refreshVideo.Add(() => { _refreshing = true; choice.Selected = Math.Max(0, Array.IndexOf(values, get(Graphics.Preset))); _refreshing = false; });
        box.AddChild(MenuKit.Row(label, choice));
    }

    private bool _refreshing;
}
