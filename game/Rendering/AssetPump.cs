using Godot;

namespace Strata.Game.Rendering;

/// <summary>Moves finished threaded loads into <see cref="AssetCache"/> every frame (added by the world launcher).</summary>
public sealed partial class AssetPump : Node
{
    public override void _Ready() => ProcessPriority = -100; // before the renderers that ask for the assets

    public override void _Process(double delta)
    {
        using var profile = Strata.Core.Diagnostics.Metrics.Frame.Time("gd.assets");
        AssetCache.Pump();
    }

    public override void _ExitTree() => AssetCache.Clear(); // the world closed: let its models go
}
