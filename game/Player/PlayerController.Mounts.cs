using Godot;
using Strata.Core.Gameplay.Mounts;

namespace Strata.Game.Player;

/// <summary>
/// Riding a mount (Slimy Saddle, Fuzzy Carrot, Honeyed Goggles, Cosmic Car Key): the quick-mount key summons or
/// dismisses one, and while mounted <see cref="MountMotion"/> (Core) replaces walking: the mount's own speed, jump and
/// flight; crouch sinks a free flier; landing on a creature stomps it (the slime) and the rider never takes fall damage.
/// </summary>
public sealed partial class PlayerController
{
    /// <summary>Handles the quick-mount key; returns true while riding (normal movement is skipped).</summary>
    private bool UpdateMount(float dt)
    {
        // As walking: a modal, a text field or the controller's screen cursor (the D-pad steps it) stops the ride and the mount key.
        bool blocked = UI.InputGate.MovementBlocked;
        if (!blocked && Input.IsActionJustPressed("quick_mount")) MountRules.QuickMount(_runtime.Game, Entity);
        if (Entity.Mount is not { } ride) return false;
        if (Entity.Minecart != null) Entity.Minecart = null;

        var input = blocked ? Vector2.Zero : Input.GetVector("move_left", "move_right", "move_forward", "move_back");
        var wish = Rig.YawBasis * new Vector3(input.X, 0, input.Y);
        bool onFloor = IsOnFloor();
        var mountInput = new MountInput(new System.Numerics.Vector3(wish.X, 0, wish.Z),
            !blocked && Input.IsActionJustPressed("jump"), !blocked && Input.IsActionPressed("jump"), !blocked && Input.IsActionPressed("crouch"));
        var v = Velocity;
        var next = MountMotion.Step(ride, new System.Numerics.Vector3(v.X, v.Y, v.Z), onFloor, mountInput, _config.Gravity, dt);
        if (!onFloor && next.Y < -2f && MountMotion.TryStomp(_runtime.Game, Entity, ride, next.Y) is { } bounce) next.Y = bounce;
        Velocity = new Vector3(next.X, next.Y, next.Z);
        CollisionMask = World.ColliderManager.TerrainLayer | World.ColliderManager.ObjectLayer |
            (next.Y <= 0.5f && !mountInput.Descend ? World.ColliderManager.PlatformLayer : 0u);
        var before = GlobalPosition;
        MoveAndSlide();
        if (IsOnFloor() && IsOnWall() && wish.LengthSquared() > 0.01f) TryStepUp(before, wish);
        if (IsOnFloor()) RefillAirMovement();
        _peakY = GlobalPosition.Y; // mounts take the landing, not the rider
        _airborne = !IsOnFloor();
        Sync();
        return true;
    }

    /// <summary>The mount being ridden, for the avatar and probes (null on foot).</summary>
    public MountRide? Mount => Entity.Mount;
}
