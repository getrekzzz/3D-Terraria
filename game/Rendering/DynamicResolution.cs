using Godot;
using Strata.Core.Presentation;

namespace Strata.Game.Rendering;

/// <summary>
/// Keeps the frame rate at 60 on slower GPUs: when the GPU is what holds frames back (its measured render time, not
/// just a slow frame, since a CPU-bound frame gains nothing from fewer pixels) the 3D resolution steps down, to at most
/// 60% of the window upscaled with FSR; when the GPU has headroom again it steps back up to the preset's render scale.
/// The decisions (windows, hysteresis, cooldowns, quantized levels) are <see cref="ResolutionGovernor"/>'s, so the
/// scale does not flip back and forth. Works with vsync (a capped 60 fps frame still shows how busy the GPU was). The
/// HUD and menus always draw at full resolution. Headless (no GPU timing) it does nothing.
/// </summary>
public sealed partial class DynamicResolution : Node
{
    private GraphicsSettings _graphics = null!;
    private ResolutionGovernor _governor = new(1f);

    /// <summary>Current 3D resolution scale (for the F3 overlay).</summary>
    public static float CurrentScale { get; private set; } = 1f;

    /// <summary>Scale changes made this session (probe <c>render.scale_changes</c>).</summary>
    public static int Changes { get; private set; }

    public void Initialize(GraphicsSettings graphics)
    {
        Name = "DynamicResolution";
        _graphics = graphics;
        _governor = new ResolutionGovernor(graphics.Preset.RenderScale);
        CurrentScale = graphics.Preset.RenderScale;
        graphics.Changed += p =>
        {
            _governor.Reset(p.RenderScale);
            CurrentScale = p.RenderScale;
        };
    }

    public override void _Ready() => FrameTiming.Enable(GetViewport());

    public override void _Process(double delta)
    {
        if (!_graphics.Preset.DynamicResolution || DisplayServer.GetName() == "headless") return;
        // Drivers without timestamp queries report 0: the governor then judges by frame times alone.
        if (!_governor.Feed(delta * 1000.0, FrameTiming.GpuMs(GetViewport()))) return;
        // The 3D buffers are reallocated at the next draw; the call count shows how often the scale changes.
        using var profile = Strata.Core.Diagnostics.Metrics.Frame.Time("gd.dynres.change");
        CurrentScale = _governor.Scale;
        Changes++;
        var viewport = GetViewport();
        if (CurrentScale < 0.999f && viewport.Scaling3DMode == Viewport.Scaling3DModeEnum.Bilinear) viewport.Scaling3DMode = Viewport.Scaling3DModeEnum.Fsr;
        viewport.Scaling3DScale = CurrentScale;
    }
}

/// <summary>Measured CPU and GPU render times of a viewport (Godot's timestamp queries).</summary>
public static class FrameTiming
{
    public static void Enable(Viewport viewport) => RenderingServer.ViewportSetMeasureRenderTime(viewport.GetViewportRid(), true);

    public static double GpuMs(Viewport viewport) => RenderingServer.ViewportGetMeasuredRenderTimeGpu(viewport.GetViewportRid());

    public static double CpuMs(Viewport viewport) => RenderingServer.ViewportGetMeasuredRenderTimeCpu(viewport.GetViewportRid());
}
