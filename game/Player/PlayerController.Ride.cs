using Godot;
using Strata.Core.Common;
using Strata.Core.Gameplay.Mounts;

namespace Strata.Game.Player;

/// <summary>Minecart riding and rope climbing.</summary>
public sealed partial class PlayerController
{
    private const float ClimbSpeed = 5f;
    private bool _climbing;

    /// <summary>Raised when the player climbs out of a minecart.</summary>
    public event System.Action? Dismounted;

    /// <summary>
    /// Rides the minecart along its track: forward/back (relative to the camera) push along the heading and jump
    /// climbs out. Returns true while riding (normal movement is skipped).
    /// </summary>
    private bool UpdateMinecart(float dt)
    {
        if (Entity.Minecart is not { } ride) return false;
        bool blocked = UI.InputGate.MovementBlocked;
        var input = MoveInput(); // reversed while confused (Status)
        var wish = Rig.YawBasis * new Vector3(input.X, 0, input.Y);
        var h = ride.Heading;
        float throttle = Mathf.Clamp(wish.Dot(new Vector3(h.X, 0, h.Z).Normalized()), -1f, 1f);
        Minecarts.Step(_runtime.Session.World, ride, throttle, dt);
        if (!blocked && Input.IsActionJustPressed("jump"))
        {
            Entity.Minecart = null;
            Velocity = new Vector3(h.X, 0, h.Z) * ride.Speed + Vector3.Up * _config.JumpSpeed;
            Dismounted?.Invoke();
            _peakY = GlobalPosition.Y;
            return false;
        }
        GlobalPosition = ToGodot(ride.Position);
        Velocity = ToGodot(ride.Heading) * ride.Speed;
        _peakY = GlobalPosition.Y;
        _airborne = false;
        RefillAirMovement();
        Sync();
        return true;
    }

    private bool _phasing;

    /// <summary>Gravity multiplier high above the world, in Space.</summary>
    private const float SpaceGravity = 0.45f;

    private bool InSpace() => GlobalPosition.Y / Units.CellSize >= _runtime.Session.Generator.Plan.Bands.SpaceStart;

    /// <summary>
    /// Shimmer's phase: while it lasts the body sinks slowly through blocks, and it keeps sinking afterwards until
    /// it is clear of them. Returns true while phasing (normal movement is skipped).
    /// </summary>
    private bool UpdatePhase()
    {
        if (!Entity.State.Effects.Phasing && !(_phasing && InsideSolid()))
        {
            _phasing = false;
            return false;
        }
        _phasing = true;
        CollisionMask = 0;
        Velocity = new Vector3(Velocity.X * 0.9f, -3f, Velocity.Z * 0.9f);
        MoveAndSlide();
        _peakY = GlobalPosition.Y;
        _airborne = false;
        Sync();
        return true;
    }

    private bool InsideSolid()
    {
        var session = _runtime.Session;
        foreach (var y in new[] { 0.2f, 1.0f, 1.7f })
        {
            ushort id = session.World.GetBlock(CellAt(GlobalPosition + Vector3.Up * y));
            if (id != 0 && id < session.Content.Blocks.Count && session.Content.Blocks[id].IsSolid) return true;
        }
        return false;
    }

    /// <summary>Whether the body is inside a climbable block (rope).</summary>
    private bool OnRope()
    {
        var session = _runtime.Session;
        foreach (var y in new[] { 0.3f, 1.2f })
        {
            ushort id = session.World.GetBlock(CellAt(GlobalPosition + Vector3.Up * y));
            if (id != 0 && id < session.Content.Blocks.Count && session.Content.Blocks[id].Climbable) return true;
        }
        return false;
    }

    /// <summary>On a rope: jump climbs, crouch slides down, otherwise hang still.</summary>
    private bool UpdateClimb(ref Vector3 v)
    {
        if (!_climbing) return false;
        bool blocked = UI.InputGate.MovementBlocked;
        bool up = !blocked && Input.IsActionPressed("jump");
        bool down = !blocked && Input.IsActionPressed("crouch");
        v.Y = up ? ClimbSpeed : down ? -ClimbSpeed : 0f;
        _peakY = GlobalPosition.Y;
        RefillAirMovement();
        return true;
    }
}
