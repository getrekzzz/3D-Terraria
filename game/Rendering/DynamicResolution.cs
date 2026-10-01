using Godot;

namespace Strata.Game.Rendering;

/// <summary>
/// Keeps the frame rate at 60 on slower GPUs: when the GPU is what holds frames back (its measured render time, not
/// just a slow frame, since a CPU-bound frame gains nothing from fewer pixels) the 3D resolution steps down, to at most
/// 60% of the window upscaled with FSR; when the GPU has headroom again it steps back up to the preset's render scale.
/// Works with vsync (a capped 60 fps frame still shows how busy the GPU was). The HUD and menus always draw at full
/// resolution.
/// </summary>
public sealed partial class DynamicResolution : Node
{
    /// <summary>Frames slower than this (~57 fps) with the GPU busy most of it lower the resolution.</summary>
    private const double SlowFrame = 1.0 / 57.0;

    /// <summary>GPU time (ms) under which the resolution may rise again, and over which a slow frame is GPU-bound.</summary>
    private const double GpuHeadroomMs = 11.0, GpuBusyMs = 13.5;

    private const float MinScale = 0.6f, Step = 0.05f;
    private const int Window = 30;

    private GraphicsSettings _graphics = null!;
    private double _sum, _gpu;
    private int _frames;
    private float _scale = 1f;

    /// <summary>Current 3D resolution scale (for the F3 overlay).</summary>
    public static float CurrentScale { get; private set; } = 1f;

    public void Initialize(GraphicsSettings graphics)
    {
        Name = "DynamicResolution";
        _graphics = graphics;
        _scale = CurrentScale = graphics.Preset.RenderScale;
        graphics.Changed += p => _scale = CurrentScale = p.RenderScale;
    }

    public override void _Ready() => FrameTiming.Enable(GetViewport());

    public override void _Process(double delta)
    {
        var preset = _graphics.Preset;
        if (!preset.DynamicResolution || DisplayServer.GetName() == "headless") return;
        _sum += delta;
        _gpu += FrameTiming.GpuMs(GetViewport());
        if (++_frames < Window) return;
        double average = _sum / _frames, gpu = _gpu / _frames;
        _sum = _gpu = 0;
        _frames = 0;
        // Drivers without timestamp queries report 0: fall back to frame times alone.
        bool measured = gpu > 0.01;
        bool gpuBound = !measured || gpu > GpuBusyMs;
        bool headroom = measured ? gpu < GpuHeadroomMs : average < 1.0 / 72.0;
        float target = _scale;
        if (average > SlowFrame && gpuBound)
            target = Mathf.Max(MinScale, _scale - (average > 1.0 / 40.0 ? Step * 2f : Step)); // well below target: step faster
        else if (headroom)
            target = Mathf.Min(preset.RenderScale, _scale + Step);
        if (Mathf.IsEqualApprox(target, _scale)) return;
        // The 3D buffers are reallocated at the next draw; the call count shows how often the scale flips.
        using var profile = Strata.Core.Diagnostics.Metrics.Frame.Time("gd.dynres.change");
        _scale = CurrentScale = target;
        var viewport = GetViewport();
        if (_scale < 0.999f && viewport.Scaling3DMode == Viewport.Scaling3DModeEnum.Bilinear) viewport.Scaling3DMode = Viewport.Scaling3DModeEnum.Fsr;
        viewport.Scaling3DScale = _scale;
    }
}

/// <summary>Measured CPU and GPU render times of a viewport (Godot's timestamp queries).</summary>
public static class FrameTiming
{
    public static void Enable(Viewport viewport) => RenderingServer.ViewportSetMeasureRenderTime(viewport.GetViewportRid(), true);

    public static double GpuMs(Viewport viewport) => RenderingServer.ViewportGetMeasuredRenderTimeGpu(viewport.GetViewportRid());

    public static double CpuMs(Viewport viewport) => RenderingServer.ViewportGetMeasuredRenderTimeCpu(viewport.GetViewportRid());
}
