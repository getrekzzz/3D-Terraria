using Godot;
using Strata.Core.Presentation;
using Strata.Game.Settings;

namespace Strata.Game.Rendering;

/// <summary>
/// The colour-blind filter (X-12): a full-screen pass over the world view (the 3D frame and the hurt flash, under the
/// HUD at layer 10, so text stays crisp and takes the interface palette instead, <see cref="UI.UiTheme.Vision"/>) that
/// daltonises it for the chosen deficiency: <see cref="ColorVision.Correction"/>'s matrix on linear RGB, at the
/// setting's strength. It lives on the game root, so the title screen's world and every world get it; with the mode off
/// the pass is hidden and costs nothing (no screen copy).
/// </summary>
public sealed partial class ColorBlindFilter : CanvasLayer
{
    public const int CanvasLayerIndex = 9;

    private const string Code = @"shader_type canvas_item;
render_mode unshaded;
uniform sampler2D screen_tex : hint_screen_texture, filter_nearest;
uniform vec3 row_r = vec3(1.0, 0.0, 0.0);
uniform vec3 row_g = vec3(0.0, 1.0, 0.0);
uniform vec3 row_b = vec3(0.0, 0.0, 1.0);
vec3 to_linear(vec3 c) { return mix(c / 12.92, pow((c + 0.055) / 1.055, vec3(2.4)), step(vec3(0.04045), c)); }
vec3 to_srgb(vec3 c) { return mix(c * 12.92, 1.055 * pow(c, vec3(1.0 / 2.4)) - 0.055, step(vec3(0.0031308), c)); }
void fragment() {
    vec3 c = to_linear(clamp(textureLod(screen_tex, SCREEN_UV, 0.0).rgb, 0.0, 1.0));
    vec3 o = clamp(vec3(dot(row_r, c), dot(row_g, c), dot(row_b, c)), 0.0, 1.0);
    COLOR = vec4(to_srgb(o), 1.0);
}";

    private readonly ShaderMaterial _material = new() { Shader = new Shader { Code = Code } };
    private ColorRect _rect = null!;

    /// <summary>The deficiency corrected for now (Off: the pass is hidden).</summary>
    public ColorBlindMode Mode { get; private set; }

    public float Strength { get; private set; }

    /// <summary>True while the pass draws (a mode is on at a strength above zero).</summary>
    public bool Active => _rect.Visible;

    public static ColorBlindFilter? Instance { get; private set; }

    public override void _Ready()
    {
        Name = "ColorBlindFilter";
        Layer = CanvasLayerIndex;
        _rect = new ColorRect { Material = _material, MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        _rect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_rect);
        Instance = this;
        Apply(UserSettings.Current);
        UserSettings.Current.Changed += Apply;
    }

    public override void _ExitTree()
    {
        UserSettings.Current.Changed -= Apply;
        if (Instance == this) Instance = null;
    }

    private void Apply(UserSettings s)
    {
        var mode = ColorVision.Parse(s.ColorBlindMode);
        float strength = Mathf.Clamp(s.ColorBlindStrength, 0f, 1f);
        if (mode == Mode && Mathf.IsEqualApprox(strength, Strength) && _rect.Visible == (mode != ColorBlindMode.Off && strength > 0f)) return;
        Mode = mode;
        Strength = strength;
        var m = ColorVision.Correction(mode, strength);
        _material.SetShaderParameter("row_r", new Vector3(m[0], m[1], m[2]));
        _material.SetShaderParameter("row_g", new Vector3(m[3], m[4], m[5]));
        _material.SetShaderParameter("row_b", new Vector3(m[6], m[7], m[8]));
        _rect.Visible = mode != ColorBlindMode.Off && strength > 0f;
    }
}
