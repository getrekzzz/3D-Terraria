using System.Text;
using Godot;
using Strata.Core.Common;
using Strata.Core.Diagnostics;
using Strata.Game.Rendering;
using Strata.Game.World;

namespace Strata.Game.UI;

/// <summary>
/// F3 overlay: frame timing and hitches (the frame profiler runs while it is open), streaming and job queues, rendering
/// counters, memory and location info.
/// </summary>
public sealed partial class PerfOverlay : CanvasLayer
{
    private WorldRuntime _runtime = null!;
    private TileObjectRenderer? _objects;
    private Label _label = null!;
    private double _timer;
    private bool _profiling; // the overlay turned the frame profiler on (not perf start), so it turns it off again
    private int _resets; // Metrics.Frame.Resets then: a `perf start` since then owns the session

    public void Initialize(WorldRuntime runtime, TileObjectRenderer? objects)
    {
        _runtime = runtime;
        _objects = objects;
        Name = "PerfOverlay";
        Layer = 40;
        _label = new Label { Position = new Vector2(16, 60), Visible = false, Theme = UiTheme.Theme };
        _label.AddThemeFontSizeOverride("font_size", 13);
        AddChild(_label);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (InputGate.TextEntry || !e.IsActionPressed("debug_overlay")) return;
        _label.Visible = !_label.Visible;
        Profile(_label.Visible);
        GetViewport().SetInputAsHandled();
    }

    public override void _ExitTree() => Profile(false);

    /// <summary>
    /// Timers and the spike log run while the overlay is open, unless <c>perf start</c> runs them: started before the
    /// overlay opened or while it was open, that session keeps running when it closes.
    /// </summary>
    private void Profile(bool on)
    {
        var m = Metrics.Frame;
        if (on && !m.TrackFrames)
        {
            m.Enabled = true;
            m.TrackFrames = true;
            _profiling = true;
            _resets = m.Resets;
        }
        else if (!on && _profiling)
        {
            _profiling = false;
            if (m.Resets != _resets) return;
            m.Enabled = false;
            m.TrackFrames = false;
        }
    }

    public override void _Process(double delta)
    {
        if (!_label.Visible) return;
        _timer -= delta;
        if (_timer > 0) return;
        _timer = 0.25;
        var s = _runtime.Streamer;
        var session = _runtime.Session;
        var focus = _runtime.FocusCell;
        var ctx = session.ContextAt(focus);
        var light = LightProbe.Sample(session.World, (_runtime.Focus?.GlobalPosition ?? Vector3.Zero) + Vector3.Up);
        var sb = new StringBuilder();
        sb.AppendLine($"FPS {Engine.GetFramesPerSecond():0}  frame {Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000:0.0} ms  physics {Performance.GetMonitor(Performance.Monitor.TimePhysicsProcess) * 1000:0.0} ms");
        var viewport = GetViewport();
        sb.AppendLine($"GPU {FrameTiming.GpuMs(viewport):0.0} ms  render CPU {FrameTiming.CpuMs(viewport):0.0} ms  3D scale {DynamicResolution.CurrentScale * 100:0}%  preset {_runtime.Services.Graphics.Name}");
        AppendSpikes(sb);
        sb.AppendLine($"chunks loaded {s.LoadedCount} meshed {s.MeshedCount}  in flight: loads {s.LoadsInFlight} meshes {s.MeshesInFlight}");
        sb.AppendLine($"jobs pending {_runtime.Jobs.Pending} running {_runtime.Jobs.Running}  uploads {_runtime.Renderer.PendingUploads}  colliders {_runtime.Colliders.BodyCount}");
        sb.AppendLine($"draw calls {RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalDrawCallsInFrame)}  primitives {RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalPrimitivesInFrame) / 1000}k  objects {_objects?.InstanceCount ?? 0}");
        sb.AppendLine($"video mem {RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.VideoMemUsed) / (1024 * 1024)} MB  static {OS.GetStaticMemoryUsage() / (1024 * 1024)} MB  managed {System.GC.GetTotalMemory(false) / (1024 * 1024)} MB");
        sb.AppendLine($"cell {focus}  chunk {ChunkMath.CellToChunk(focus)}  {string.Join('/', ctx.Biomes)} {ctx.Layer} depth {ctx.Depth:0}");
        sb.AppendLine($"light sky {light.A * 15:0} block {light.R * 15:0}/{light.G * 15:0}/{light.B * 15:0}  time {session.Clock.Hour:00.00}h {(session.Clock.IsDay ? "day" : "night")} moon {session.Clock.MoonPhase}");
        if (_runtime.Weather is { } w)
            sb.AppendLine($"weather {session.Weather.Precipitation} {session.Weather.Current:0.00} here {w.Local}  wind {session.Weather.WindSpeed:0.00}  particles {w.ActiveParticles}/{w.BudgetTotal}  ambient {_runtime.Ambience?.Particles.Alive ?? 0}  wet {w.Wetness:0.00}");
        _label.Text = sb.ToString();
    }

    /// <summary>"spikes >33 ms N (worst X) · GC a/b/c pause y ms" and the last spike's frame time and top timers.</summary>
    private static void AppendSpikes(StringBuilder sb)
    {
        var log = Metrics.Frame.Spikes;
        var (gen0, gen1, gen2) = log.Collections;
        sb.AppendLine($"spikes >33 ms {log.Over33} (>50 {log.Over50}, worst {log.WorstMs:0}) of {log.Frames} frames{(log.MainThreadOnly ? " (main thread)" : "")} · p99 {log.Percentile(0.99):0.0} ms · GC {gen0}/{gen1}/{gen2} pause {log.GcPauseMs:0.0} ms");
        var last = log.Recent(1);
        if (last.Count > 0) sb.AppendLine($"last: {last[0].Describe()}");
    }
}
