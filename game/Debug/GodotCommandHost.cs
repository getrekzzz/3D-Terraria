using Strata.Core.Commands;
using Strata.Core.Diagnostics;
using Strata.Core.Session;
using Strata.Game.Bootstrap;
using Strata.Game.Player;
using Strata.Game.World;

namespace Strata.Game.Debug;

/// <summary>Command host for the running game: moves the physical body and camera, and adds engine probes.</summary>
public sealed class GodotCommandHost : ICommandHost
{
    private readonly PlayerController? _player;

    public GodotCommandHost(PlayerController? player) => _player = player;

    public void Teleport(PlayerEntity player, System.Numerics.Vector3 feet, bool settle = false)
    {
        if (_player != null && _player.Entity == player) _player.Teleport(feet, settle: settle);
        else player.Position = feet;
    }

    public void Aim(PlayerEntity player, System.Numerics.Vector3 direction)
    {
        var dir = System.Numerics.Vector3.Normalize(direction);
        player.Aim = dir;
        if (_player != null && _player.Entity == player)
            _player.Rig.LookToward(_player.Rig.GlobalPosition + new Godot.Vector3(dir.X, dir.Y, dir.Z));
    }

    /// <summary>
    /// Creates the command context for a launched world with engine-side probes: the core engine counters below, then
    /// every <see cref="ProbeSourceAttribute"/> class of the game assembly (its <c>Register</c> may take the
    /// <see cref="LaunchedWorld"/> and the <see cref="GodotLog"/>).
    /// </summary>
    public static CommandContext CreateContext(LaunchedWorld world, GodotLog log, CommandRegistry registry)
    {
        var ctx = new CommandContext(world.Runtime.Game, new GodotCommandHost(world.Player)) { Registry = registry };
        var runtime = world.Runtime;
        ctx.AddProbe("errors", () => log.Errors);
        ctx.AddProbe("warnings", () => log.Warnings);
        ctx.AddProbe("colliders.bodies", () => runtime.Colliders.BodyCount);
        ctx.AddProbe("uploads.pending", () => runtime.Renderer.PendingUploads);
        ctx.AddProbe("chunks.hidden", () => runtime.Renderer.HiddenCount);
        ctx.AddProbe("chunks.triangles", () => runtime.Renderer.ShownTriangles);
        ctx.AddProbe("fps", () => Godot.Engine.GetFramesPerSecond());
        ctx.AddProbe("props.problems", () =>
        {
            foreach (var p in world.Props.Problems) log.Warn("props", p);
            return world.Props.Problems.Count;
        });
        ctx.AddProbe("objects.instances", () => world.Objects.InstanceCount);
        ProbeSources.RegisterAll(ctx, typeof(GodotCommandHost).Assembly, world, log);
        return ctx;
    }
}
