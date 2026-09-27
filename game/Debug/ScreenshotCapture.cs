using Godot;
using Strata.Game.World;

namespace Strata.Game.Debug;

/// <summary>
/// Waits until streaming and uploads are idle (plus a settle delay), saves a screenshot and optionally quits.
/// Driven by <c>--screenshot=path.png</c> (used by CI and visual QA), or re-armed by the <c>screenshot</c> command for
/// scripted tours.
/// </summary>
public sealed partial class ScreenshotCapture : Node
{
    private double _idleTime;
    private double _elapsed;

    public WorldRuntime World { get; set; } = null!;
    public string OutputPath { get; set; } = "user://screenshot.png";
    public double SettleSeconds { get; set; } = 1.5;
    public double TimeoutSeconds { get; set; } = 240;
    public bool QuitAfter { get; set; } = true;

    /// <summary>Extra readiness condition (e.g. a startup script has finished).</summary>
    public System.Func<bool>? WaitFor { get; set; }

    public event System.Action<string>? Captured;

    /// <summary>A capture is requested and not yet written.</summary>
    public bool Armed { get; set; } = true;

    /// <summary>Starts waiting for the next idle moment to capture <paramref name="path"/>.</summary>
    public void Request(string path)
    {
        OutputPath = path;
        _idleTime = 0;
        _elapsed = 0;
        _nextReport = 5;
        Armed = true;
        SetProcess(true);
    }

    public override void _Ready() => SetProcess(Armed);

    private double _nextReport = 5;

    public override void _Process(double delta)
    {
        _elapsed += delta;
        if (_elapsed >= _nextReport)
        {
            _nextReport += 5;
            var s = World.Streamer;
            GD.Print($"[stream] t={_elapsed:F0}s loaded={s.LoadedCount} meshed={s.MeshedCount} loads={s.LoadsInFlight} meshes={s.MeshesInFlight} jobs={World.Jobs.Pending}/{World.Jobs.Running} uploads={World.Renderer.PendingUploads} idle={s.IsIdle} {(s.IsIdle ? "" : s.DebugState())} completions={World.Jobs.Completions.Count}");
        }
        bool idle = World.Streamer.IsIdle && World.Renderer.PendingUploads == 0 && World.Jobs.Idle && (WaitFor?.Invoke() ?? true);
        _idleTime = idle ? _idleTime + delta : 0;
        if (_idleTime < SettleSeconds && _elapsed < TimeoutSeconds) return;
        SetProcess(false);
        CallDeferred(nameof(Capture));
    }

    private async void Capture()
    {
        if (DisplayServer.GetName() == "headless")
        {
            GD.Print("SCREENSHOT skipped (headless renderer)");
            Armed = false;
            Captured?.Invoke("");
            if (QuitAfter) GetTree().Quit(0);
            return;
        }
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var image = GetViewport().GetTexture().GetImage();
        var path = ProjectSettings.GlobalizePath(OutputPath);
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        var err = image.SavePng(path);
        GD.Print(err == Error.Ok ? $"SCREENSHOT {path}" : $"Screenshot failed: {err}");
        Armed = false;
        Captured?.Invoke(path);
        if (QuitAfter) GetTree().Quit(err == Error.Ok ? 0 : 1);
    }
}
