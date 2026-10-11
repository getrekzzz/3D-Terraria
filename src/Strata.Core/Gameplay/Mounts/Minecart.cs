using System.Numerics;
using Strata.Core.Common;
using Strata.Core.Content.Specs;
using Strata.Core.Voxels;

namespace Strata.Core.Gameplay.Mounts;

/// <summary>
/// A minecart riding along track pieces. The cart sits in a track cell heading along <see cref="Dir"/> toward
/// <see cref="Next"/>; <see cref="Progress"/> runs 0→1 between the two cells' centres. Tracks connect straight ahead,
/// one step up or down, and round a corner when the way ahead ends beside a crossing piece.
/// </summary>
public sealed class MinecartRide
{
    public MinecartRide(MinecartParams config, Int3 cell, Int3 dir)
    {
        Config = config;
        Cell = cell;
        Dir = dir;
    }

    public MinecartParams Config { get; }
    public Int3 Cell { get; internal set; }
    public Int3 Dir { get; internal set; }
    public Int3? Next { get; internal set; }
    public Int3 NextDir { get; internal set; }
    public float Progress { get; internal set; }

    /// <summary>Top speed and acceleration multiplier (1.25 for the upgraded Mechanical Cart).</summary>
    public float SpeedScale { get; set; } = 1f;

    /// <summary>Speed along the track in m/s (never negative; reversing turns the cart round).</summary>
    public float Speed { get; internal set; }

    /// <summary>Feet position of the rider in meters.</summary>
    public Vector3 Position
    {
        get
        {
            var a = Seat(Cell);
            return Next is { } n ? Vector3.Lerp(a, Seat(n), Progress) : a;
        }
    }

    /// <summary>Unit heading in meters (for facing the cart).</summary>
    public Vector3 Heading => Next is { } n ? Vector3.Normalize(Seat(n) - Seat(Cell)) : new Vector3(Dir.X, 0, Dir.Z);

    private static Vector3 Seat(Int3 c) => new Vector3(c.X + 0.5f, c.Y + 0.1f, c.Z + 0.5f) * Units.CellSize;
}

public static class Minecarts
{
    private const float Friction = 1.5f;
    private const float SlopeGravity = 7f;

    public static bool IsTrack(VoxelWorld world, Int3 cell) => world.TileObjects.At(cell)?.Def.Track == true;

    /// <summary>Boards a cart on the nearest track piece at or just around the feet, heading the way the player looks.</summary>
    public static MinecartRide? Board(VoxelWorld world, Int3 feet, Vector3 aim, MinecartParams config)
    {
        Int3? found = null;
        foreach (var dy in new[] { 0, -1, 1 })
            for (int dz = -1; dz <= 1 && found == null; dz++)
                for (int dx = -1; dx <= 1 && found == null; dx++)
                {
                    var c = feet + new Int3(dx, dy, dz);
                    if (IsTrack(world, c)) found = c;
                }
        if (found is not { } cell) return null;
        var piece = world.TileObjects.At(cell)!;
        bool alongX = (piece.Rotation & 1) == 1;
        var dir = alongX ? new Int3(aim.X >= 0 ? 1 : -1, 0, 0) : new Int3(0, 0, aim.Z >= 0 ? 1 : -1);
        var ride = new MinecartRide(config, cell, dir);
        Link(world, ride);
        return ride;
    }

    /// <summary>
    /// Advances the ride one step. <paramref name="throttle"/> (−1…1) pushes along the heading; pushing back while
    /// stopped turns the cart round.
    /// </summary>
    public static void Step(VoxelWorld world, MinecartRide ride, float throttle, float dt)
    {
        var cfg = ride.Config;
        if (ride.Speed <= 0.01f && throttle < -0.1f && ride.Progress <= 0.01f)
        {
            ride.Dir = -ride.Dir;
            Link(world, ride);
            throttle = -throttle;
        }
        float speed = ride.Speed + throttle * cfg.Acceleration * ride.SpeedScale * dt;
        if (MathF.Abs(throttle) < 0.1f) speed -= Friction * dt;
        if (ride.Next is { } n) speed += (ride.Cell.Y - n.Y) * SlopeGravity * dt;
        ride.Speed = Math.Clamp(speed, 0f, cfg.MaxSpeed * ride.SpeedScale);
        if (ride.Next == null)
        {
            ride.Speed = 0;
            ride.Progress = 0;
            return;
        }
        ride.Progress += ride.Speed * dt / Units.CellSize;
        while (ride.Progress >= 1f && ride.Next is { } next)
        {
            ride.Progress -= 1f;
            ride.Cell = next;
            ride.Dir = ride.NextDir;
            Link(world, ride);
            if (ride.Next == null)
            {
                ride.Progress = 0;
                ride.Speed = 0; // end of the line
            }
        }
    }

    /// <summary>Finds the piece after the current one along the heading.</summary>
    private static void Link(VoxelWorld world, MinecartRide ride)
    {
        var c = ride.Cell;
        var d = ride.Dir;
        foreach (var candidate in new[] { c + d, c + d + Int3.Up, c + d - Int3.Up })
            if (IsTrack(world, candidate))
            {
                ride.Next = candidate;
                ride.NextDir = d;
                return;
            }
        foreach (var turn in new[] { new Int3(-d.Z, 0, d.X), new Int3(d.Z, 0, -d.X) })
            if (IsTrack(world, c + turn))
            {
                ride.Next = c + turn;
                ride.NextDir = turn;
                return;
            }
        ride.Next = null;
        ride.NextDir = d;
    }
}
