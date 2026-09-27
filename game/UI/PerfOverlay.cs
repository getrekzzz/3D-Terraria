using System.Text;
using Godot;
using Strata.Core.Common;
using Strata.Game.Rendering;
using Strata.Game.World;

namespace Strata.Game.UI;

/// <summary>F3 overlay: frame timing, streaming and job queues, rendering counters, memory and location info.</summary>
public sealed partial class PerfOverlay : CanvasLayer
{
    private WorldRuntime _runtime = null!;
    private TileObjectRenderer? _objects;
    private Label _label = null!;
    private double _timer;

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
        GetViewport().SetInputAsHandled();
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
        sb.AppendLine($"chunks loaded {s.LoadedCount} meshed {s.MeshedCount}  in flight: loads {s.LoadsInFlight} meshes {s.MeshesInFlight}");
        sb.AppendLine($"jobs pending {_runtime.Jobs.Pending} running {_runtime.Jobs.Running}  uploads {_runtime.Renderer.PendingUploads}  colliders {_runtime.Colliders.BodyCount}");
        sb.AppendLine($"draw calls {RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalDrawCallsInFrame)}  primitives {RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalPrimitivesInFrame) / 1000}k  objects {_objects?.InstanceCount ?? 0}");
        sb.AppendLine($"video mem {RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.VideoMemUsed) / (1024 * 1024)} MB  static {OS.GetStaticMemoryUsage() / (1024 * 1024)} MB  managed {System.GC.GetTotalMemory(false) / (1024 * 1024)} MB");
        sb.AppendLine($"cell {focus}  chunk {ChunkMath.CellToChunk(focus)}  {string.Join('/', ctx.Biomes)} {ctx.Layer} depth {ctx.Depth:0}");
        sb.AppendLine($"light sky {light.A * 15:0} block {light.R * 15:0}/{light.G * 15:0}/{light.B * 15:0}  time {session.Clock.Hour:00.00}h {(session.Clock.IsDay ? "day" : "night")} moon {session.Clock.MoonPhase}");
        _label.Text = sb.ToString();
    }
}
