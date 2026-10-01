using Godot;
using Strata.Core.Diagnostics;

namespace Strata.Game.Debug;

/// <summary>
/// Closes each profiled frame for the spike log: it processes after every other node and hands
/// <see cref="Metrics.Frame"/> the real time since its previous call (the last draw, this frame's physics ticks and
/// process), so the timers recorded in between are what that frame spent. Costs one clock read while profiling is off;
/// keeps running while the game is paused so a pause never reads as one long frame.
/// On a software renderer (lavapipe/llvmpipe takes seconds to draw a frame, so every frame would be a spike) it
/// measures from the end of the last draw instead: the main thread's own work, where hitches still show.
/// </summary>
public sealed partial class FrameProbe : Node
{
    private ulong _last, _drawn;
    private bool _software;

    public FrameProbe()
    {
        Name = "FrameProbe";
        ProcessPriority = int.MaxValue;
        ProcessMode = ProcessModeEnum.Always;
    }

    public override void _Ready()
    {
        _software = RenderingServer.GetVideoAdapterName().Contains("llvmpipe", System.StringComparison.OrdinalIgnoreCase);
        Metrics.Frame.Spikes.MainThreadOnly = _software;
        if (_software) RenderingServer.FramePostDraw += OnDrawn;
    }

    public override void _ExitTree()
    {
        if (_software) RenderingServer.FramePostDraw -= OnDrawn;
    }

    public override void _Process(double delta)
    {
        ulong now = Time.GetTicksUsec();
        ulong from = _software && _drawn > _last ? _drawn : _last;
        if (_last != 0) Metrics.Frame.EndFrame((now - from) / 1000.0);
        _last = now;
    }

    private void OnDrawn() => _drawn = Time.GetTicksUsec();
}
