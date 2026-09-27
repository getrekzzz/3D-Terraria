using Godot;

namespace Strata.Game.Rendering;

/// <summary>
/// Keeps the frame rate up on slower GPUs: when frames average slower than ~50 fps the 3D resolution steps down
/// (to at most 60% of the window, upscaled with FSR); when they run comfortably fast it steps back up to the preset's
/// render scale. The HUD and menus always draw at full resolution.
/// </summary>
public sealed partial class DynamicResolution : Node
{
    private const double SlowFrame = 1.0 / 50.0, FastFrame = 1.0 / 72.0;
    private const float MinScale = 0.6f, Step = 0.05f;

    private GraphicsSettings _graphics = null!;
    private double _sum;
    private int _frames;
    private float _scale = 1f;

    public void Initialize(GraphicsSettings graphics)
    {
        Name = "DynamicResolution";
        _graphics = graphics;
        _scale = graphics.Preset.RenderScale;
        graphics.Changed += p => _scale = p.RenderScale;
    }

    public override void _Process(double delta)
    {
        var preset = _graphics.Preset;
        if (!preset.DynamicResolution || DisplayServer.GetName() == "headless") return;
        _sum += delta;
        if (++_frames < 45) return;
        double average = _sum / _frames;
        _sum = 0;
        _frames = 0;
        float target = _scale;
        if (average > SlowFrame) target = Mathf.Max(MinScale, _scale - Step);
        else if (average < FastFrame) target = Mathf.Min(preset.RenderScale, _scale + Step);
        if (Mathf.IsEqualApprox(target, _scale)) return;
        _scale = target;
        var viewport = GetViewport();
        if (_scale < 0.999f && viewport.Scaling3DMode == Viewport.Scaling3DModeEnum.Bilinear) viewport.Scaling3DMode = Viewport.Scaling3DModeEnum.Fsr;
        viewport.Scaling3DScale = _scale;
    }
}
