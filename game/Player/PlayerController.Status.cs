using Godot;

namespace Strata.Game.Player;

/// <summary>Status effects that change how the controls behave (Confused reverses the movement keys).</summary>
public sealed partial class PlayerController
{
    /// <summary>
    /// The movement keys read through the player's status: reversed while Confused (strafe always, forward/back unless
    /// the effect says otherwise). Only the body's movement is affected, never the camera; a dead player is never steered.
    /// </summary>
    private Vector2 MoveInput()
    {
        if (UI.InputGate.GameplayBlocked) return Vector2.Zero;
        var input = Input.GetVector("move_left", "move_right", "move_forward", "move_back");
        var fx = Entity.State.Effects;
        if (!fx.InvertControls || !Entity.Alive) return input;
        var (x, y) = fx.SteerInput(input.X, input.Y);
        return new Vector2(x, y);
    }

    /// <summary>A key's local direction (x = right, z = back) through the player's status: a confused dash goes the other way.</summary>
    private Vector3 StatusDirection(Vector3 local)
    {
        var fx = Entity.State.Effects;
        if (!fx.InvertControls) return local;
        var (x, z) = fx.SteerInput(local.X, local.Z);
        return new Vector3(x, local.Y, z);
    }
}
