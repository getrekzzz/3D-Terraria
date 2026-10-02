using Godot;

namespace Strata.Game.UI.PhotoMode;

/// <summary>
/// Photo mode's colour filter: a full-screen post pass over the rendered frame (index order of
/// <see cref="Strata.Core.Presentation.PhotoLens.Filters"/>: none, warm, cool, mono, sepia). Drawn below the overlay so
/// pictures keep it; "none" hides the pass entirely (no screen copy).
/// </summary>
public sealed partial class PhotoFilterLayer : CanvasLayer
{
    private const string Code = @"shader_type canvas_item;
uniform sampler2D screen_tex : hint_screen_texture, filter_linear_mipmap;
uniform int mode = 0;
void fragment() {
    vec3 c = textureLod(screen_tex, SCREEN_UV, 0.0).rgb;
    float l = dot(c, vec3(0.299, 0.587, 0.114));
    if (mode == 1) c = c * vec3(1.08, 1.0, 0.84) + vec3(0.015, 0.005, 0.0);
    else if (mode == 2) c = c * vec3(0.86, 0.97, 1.1);
    else if (mode == 3) c = vec3(l);
    else if (mode == 4) c = vec3(l) * vec3(1.07, 0.9, 0.68) + vec3(0.035, 0.02, 0.0);
    COLOR = vec4(c, 1.0);
}";

    private readonly ShaderMaterial _material = new() { Shader = new Shader { Code = Code } };
    private ColorRect _rect = null!;
    private int _mode;

    public int Mode
    {
        get => _mode;
        set
        {
            _mode = value;
            _material.SetShaderParameter("mode", value);
            if (_rect != null) _rect.Visible = value != 0;
        }
    }

    public override void _Ready()
    {
        Name = "PhotoFilter";
        Layer = 38;
        _rect = new ColorRect { Material = _material, MouseFilter = Control.MouseFilterEnum.Ignore, Visible = _mode != 0 };
        _rect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_rect);
    }
}
