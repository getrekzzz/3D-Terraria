using Godot;
using Strata.Core.Session;

namespace Strata.Game.Player;

/// <summary>
/// Sleeping in a bed (P3-34, rules in Core's GameWorld.Sleep): the body is held on the mattress while the player
/// sleeps, and moving, jumping, using an item or interacting gets them up. Core wakes them when hurt; the body is then
/// put back where Core stood them.
/// </summary>
public sealed partial class PlayerController
{
    private bool _wasSleeping;

    /// <summary>The physics frame the player last got up on: the press that woke them does nothing else.</summary>
    public ulong WokeFrame { get; private set; }

    /// <summary>True while asleep (normal movement is skipped).</summary>
    private bool UpdateSleep()
    {
        if (!Entity.Sleeping)
        {
            if (_wasSleeping) Stand();
            return false;
        }
        bool lyingDown = !_wasSleeping; // the press that lay the player down must not get them up again
        _wasSleeping = true;
        if (!lyingDown && !UI.InputGate.GameplayBlocked && WakeInput())
        {
            _runtime.Game.Wake(Entity);
            Stand();
            return false;
        }
        var (feet, _) = GameWorld.BedRest(Entity.SleepBed!);
        GlobalPosition = ToGodot(feet);
        Velocity = Vector3.Zero;
        _peakY = GlobalPosition.Y;
        Sync();
        return true;
    }

    private static bool WakeInput() =>
        Input.GetVector("move_left", "move_right", "move_forward", "move_back").LengthSquared() > 0.04f
        || Input.IsActionJustPressed("jump") || Input.IsActionJustPressed("use_primary")
        || Input.IsActionJustPressed("use_secondary") || Input.IsActionJustPressed("interact");

    /// <summary>Back on the feet where Core stood the player (beside the bed).</summary>
    private void Stand()
    {
        _wasSleeping = false;
        WokeFrame = Engine.GetPhysicsFrames();
        GlobalPosition = ToGodot(Entity.Position);
        Velocity = Vector3.Zero;
        _peakY = GlobalPosition.Y;
    }
}
