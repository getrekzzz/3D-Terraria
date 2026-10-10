using Godot;
using Strata.Core.Gameplay.Interaction;
using Strata.Core.Presentation;
using Strata.Game.Settings;

namespace Strata.Game.Player;

/// <summary>
/// Looking with the controller's right stick (the look_* actions, rebindable): the stick is shaped by the dead zone
/// setting and the curve of data/config/input.json ("pad") into turn and pitch speeds (<see cref="PadLook"/>), scaled by
/// the controller sensitivity and inverted on request. With aim assist on (the default for a controller; never for the
/// mouse) the creature nearest the crosshair inside a small cone, in sight, slows the turn while the crosshair is on or
/// next to it (friction), and the view is drawn gently toward it while the player is steering (magnetism,
/// <see cref="AimAssist"/>). Runs every rendered frame before the view is placed; looks for a creature only while the
/// controller is in use; photo mode and every screen that stops the mouse look stop it too (InputGate.LookBlocked).
/// </summary>
public sealed partial class CameraRig
{
    private static readonly StringName LookLeft = "look_left", LookRight = "look_right", LookUp = "look_up", LookDown = "look_down";
    private static readonly StringName MoveLeft = "move_left", MoveRight = "move_right", MoveForward = "move_forward", MoveBack = "move_back";

    private System.Func<ushort, bool>? _opaque;

    /// <summary>The creature aim assist is helping toward this frame (the <c>pad.assist_target</c> probe), if any.</summary>
    public AimCandidate? AssistTarget { get; private set; }

    /// <summary>The look speed multiplier aim assist applied this frame (1: none).</summary>
    public float AssistFriction { get; private set; } = 1f;

    /// <summary>Radians the view was drawn toward a creature in total (the <c>pad.assist_pull</c> probe).</summary>
    public float AssistPulled { get; private set; }

    private void PadLook(float dt)
    {
        AssistTarget = null;
        AssistFriction = 1f;
        if (UI.InputGate.LookBlocked || UI.InputGate.HotkeysBlocked || dt <= 0) return;
        var s = UserSettings.Current;
        var pad = Bootstrap.InputSetup.Config.Pad;
        var raw = new System.Numerics.Vector2(
            Input.GetActionRawStrength(LookRight) - Input.GetActionRawStrength(LookLeft),
            Input.GetActionRawStrength(LookDown) - Input.GetActionRawStrength(LookUp));
        var shaped = Strata.Core.Presentation.PadLook.Shape(raw, Mathf.Clamp(s.PadDeadzone, 0.05f, 0.5f), pad.OuterDeadzone, pad.Exponent);
        bool assist = s.AimAssist && UI.InputDevice.UsingPad;
        if (!assist && shaped == System.Numerics.Vector2.Zero) return;
        var rates = Strata.Core.Presentation.PadLook.Rates(shaped, pad, Mathf.Max(0.05f, s.PadLookSensitivity));
        var target = assist ? FindAssistTarget(pad.AimAssist) : null;
        if (target is { } t)
        {
            AssistTarget = t;
            AssistFriction = AimAssist.Friction(t.Angle, t.AngularRadius, pad.AimAssist);
        }
        _yaw -= rates.X * AssistFriction * dt;
        _pitch = Mathf.Clamp(_pitch - rates.Y * AssistFriction * dt * (s.PadInvertY ? -1f : 1f), -1.45f, 1.45f);
        if (target is not { } aim) return;
        // Steering is either stick: turning onto a creature, or walking while it is near the crosshair.
        float move = Input.GetVector(MoveLeft, MoveRight, MoveForward, MoveBack).Length();
        float input = Mathf.Max(shaped.Length(), move);
        var (dy, dp) = AimAssist.Pull(_yaw, _pitch, ToCore(ViewOrigin), aim.Center, aim.AngularRadius, input, dt, pad.AimAssist);
        _yaw += dy;
        _pitch = Mathf.Clamp(_pitch + dp, -1.45f, 1.45f);
        AssistPulled += Mathf.Sqrt(dy * dy + dp * dp);
    }

    /// <summary>The creature nearest the crosshair inside the cone and range, if a straight line to it is clear of terrain.</summary>
    private AimCandidate? FindAssistTarget(AimAssistConfig config)
    {
        if (_body is not PlayerController player) return null;
        var game = player.Runtime.Game;
        var origin = ToCore(ViewOrigin);
        var forward = ToCore(Forward);
        if (AimAssist.Pick(game.Npcs.All, origin, forward, config) is not { } pick) return null;
        var world = game.Session.World;
        _opaque ??= block => world.Content.Blocks[block].IsSolid;
        float cell = Strata.Core.Common.Units.CellSize;
        var to = pick.Center - origin;
        var hit = Strata.Core.Voxels.VoxelRaycast.Cast(world, origin / cell, to, (pick.Distance - 0.5f) / cell, _opaque);
        return hit == null ? pick : null;
    }

    private static System.Numerics.Vector3 ToCore(Vector3 v) => new(v.X, v.Y, v.Z);
}
