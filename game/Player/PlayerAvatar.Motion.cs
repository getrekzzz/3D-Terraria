using Godot;
using Strata.Core.Content.Defs;
using Strata.Core.Gameplay.Items;
using Strata.Core.Presentation;
using Strata.Core.Session;
using Strata.Game.Rendering;

namespace Strata.Game.Player;

/// <summary>
/// How the body moves: the chest, head and arms always face the camera's yaw (the crosshair), while the legs turn
/// toward where the player walks (at most <see cref="BodyFacing.MaxTwist"/> away) and strafe sideways or backpedal
/// (<see cref="BodyFacing"/>); locomotion follows the controller (walk, run, sprint, strafe, jump, fall, land, swim,
/// climb, glide); every item use plays its action once over the upper body, stretched to the item's use animation
/// (chained uses flow on), except a flail's or a yoyo's, which loops on the right arm while the use lasts; a carried
/// torch is held up; hits make the body flinch.
/// </summary>
public sealed partial class PlayerAvatar
{
    /// <summary>How fast (per second) the legs ease toward their yaw.</summary>
    private const float LegTurnRate = 12f;

    /// <summary>How much of the aim pitch the arms take between uses.</summary>
    private const float RestArmAim = 0.15f;

    private float _legYaw, _airTime, _fallSpeed, _landTimer;

    /// <summary>The arm aim of the style whose action is playing (use_styles.json armAim).</summary>
    private float _actionArmAim = RestArmAim;
    private int _lastSerial, _lastHurt = int.MaxValue;
    private Stride _stride;

    /// <summary>How the legs carry the body (forward, strafing or backpedalling); forward while standing.</summary>
    public Stride Stride => _stride;

    /// <summary>The torso's twist from the legs onto the facing (degrees, + = to the left): the legs turn the other way.</summary>
    public float TwistDegrees => Mathf.RadToDeg(_visual?.Humanoid?.Aim?.AppliedTwist ?? 0f);

    /// <summary>Chained uses whose upper-body action dipped toward the locomotion pose in between (0 when they flow on).</summary>
    public int ActionDips => _visual?.Humanoid?.ActionDips ?? 0;

    /// <summary>How much of the upper body the item action holds (0–1).</summary>
    public float ActionWeight => _visual?.Humanoid?.ActionWeight ?? 0f;

    /// <summary>How far the right arm's looping clip (a raised torch, a spun flail, a worked yoyo) is blended in (0–1).</summary>
    public float HoldWeight => _visual?.Humanoid?.HoldWeight ?? 0f;

    /// <summary>Which looping clip the right arm holds: 0 a raised light, 1 a flail, 2 a yoyo, −1 none.</summary>
    public int HoldClip => _visual?.Humanoid?.HoldIndex ?? -1;

    /// <summary>
    /// How far (degrees) the chest is turned from where the player looks: the body's yaw plus the torso twist the aim
    /// applies, against the horizontal <see cref="PlayerEntity.Facing"/> (the camera's forward). About 0 while alive.
    /// </summary>
    public float FacingErrorDegrees
    {
        get
        {
            var f = _controller.Entity.Facing;
            float facing = f.X * f.X + f.Z * f.Z > 1e-6f ? Mathf.Atan2(-f.X, -f.Z) : _controller.Rig.Yaw;
            float chest = Rotation.Y + (_visual?.Humanoid?.Aim?.AppliedTwist ?? 0f);
            return Mathf.RadToDeg(Mathf.Abs(Mathf.AngleDifference(chest, facing)));
        }
    }

    private void Animate(PlayerEntity entity, float dt)
    {
        float facing = _controller.Rig.Yaw;
        var h = _visual!.Humanoid;
        if (h == null)
        {
            if (entity.Alive) _legYaw = BodyFacing.Turn(_legYaw, facing, facing, LegTurnRate, dt);
            Rotation = new Vector3(0, _legYaw, 0);
            _visual.Play(ChooseClip(entity, out float speed), speed: speed);
            return;
        }
        var v = _controller.Velocity;
        float ground = entity.Minecart != null ? 0f : new Vector2(v.X, v.Z).Length();
        bool upright = _controller.Swimming || _controller.Climbing || !_controller.IsOnFloor();
        TurnLegs(h, entity, facing, v, ground, upright, dt);
        Rotation = new Vector3(0, _legYaw, 0);

        string state = Locomotion(entity, h, v, ground, dt, out float rate);
        h.Locomotion(state, rate);

        // Item actions: each new use animation (its serial changes) plays the item's action once, over the whole
        // animation after speed bonuses; tool hits and gun shots inside one animation never restart it. A use quicker
        // than the clip's top rate (use_styles.json) starts past the wind-up, so the strike still lands inside it.
        // A flail's or a yoyo's clip loops on the right arm for as long as its use lasts instead.
        string? looped = null;
        if (_heldItem != null && entity.Alive && entity.UseAnimation > 0)
        {
            var info = _game.Uses[_heldItem];
            string action = ActionFor(_game, info);
            bool loops = HumanoidAnimator.Holds(action);
            if (entity.UseSerial != _lastSerial)
            {
                _actionArmAim = _game.Styles.Style(info.Style).ArmAim;
                var timing = _game.ItemUse.Timing;
                if (!loops) h.Play(action, entity.UseAnimationMax / (float)Strata.Core.Common.Units.TicksPerSecond, timing.MaxClipRate, timing.StrikeStart(action));
            }
            if (entity.UseItem == _heldItem)
            {
                if (loops) looped = action;
                else h.Sustain(); // timed by the simulation
            }
        }
        _lastSerial = entity.UseSerial;
        int sinceHurt = entity.State.Vitals.TicksSinceHurt;
        if (sinceHurt < _lastHurt && entity.Alive) h.Hurt();
        _lastHurt = sinceHurt;
        h.Hold(looped ?? (_heldItem?.HeldLight != null && h.Action == null && entity.Alive ? "hold" : null));

        if (h.Aim is { } aimMod)
        {
            var aim = entity.Aim;
            aimMod.Twist = entity.Alive ? BodyFacing.Twist(_legYaw, facing) : 0f;
            aimMod.Pitch = entity.Alive ? Mathf.Atan2(aim.Y, new Vector2(aim.X, aim.Z).Length()) : 0f;
            aimMod.ArmAim = h.Action != null || looped != null ? _actionArmAim : RestArmAim;
        }
        if (entity.Sleeping) LieInBed(entity, h, dt);
    }

    /// <summary>
    /// The legs (the whole model; the torso twists back to the facing): moving on the ground they turn toward the
    /// movement and pick the stride that carries the feet along it (forward gait, strafe clip or backpedal); standing,
    /// in the air, in water, on a rope or in a minecart they face the camera. Always within the twist limit of the
    /// facing, so the chest stays on the crosshair however fast the camera turns. A dead body keeps its yaw.
    /// </summary>
    private void TurnLegs(HumanoidAnimator h, PlayerEntity entity, float facing, Vector3 v, float ground, bool upright, float dt)
    {
        if (!entity.Alive) return;
        float target = facing;
        if (!upright && ground > 0.6f)
        {
            float strafe = h.CanStrafe ? Mathf.DegToRad(HumanoidAnimator.StrafeAngle) : 0f;
            target = BodyFacing.MovingLegs(facing, Mathf.Atan2(-v.X, -v.Z), strafe, _stride, out _stride);
        }
        else
        {
            _stride = Stride.Forward;
        }
        _legYaw = BodyFacing.Turn(_legYaw, target, facing, LegTurnRate, dt);
    }

    private string Locomotion(PlayerEntity entity, HumanoidAnimator h, Vector3 v, float ground, float dt, out float rate)
    {
        rate = 1f;
        if (!entity.Alive) return "death";
        if (entity.Sleeping) return h.Has("sleep") ? "sleep" : "idle"; // in a bed (PlayerAvatar.Sleep.cs)
        if (entity.Mount != null) return h.Has("sit") ? "sit" : "idle"; // in the saddle (PlayerAvatar.Mount.cs)
        if (entity.Minecart != null) return "idle"; // standing in the cart (the body is moved along the track)
        if (_controller.Swimming)
        {
            rate = 0.7f + Mathf.Min(v.Length(), 4f) * 0.2f;
            return "swim";
        }
        if (_controller.Climbing)
        {
            rate = Mathf.Clamp(Mathf.Abs(v.Y) / 2.5f, 0f, 1.6f);
            return "climb";
        }
        if (!_controller.IsOnFloor() && !_controller.Frozen)
        {
            _airTime += dt;
            _fallSpeed = Mathf.Min(_fallSpeed, v.Y);
            if (_controller.Gliding || _controller.Flying) return "glide";
            return v.Y > 0.5f || (h.State == "jump" && v.Y > -2f) ? "jump" : "fall";
        }
        if (_airTime > 0.3f && _fallSpeed < -6f && ground < 3f) _landTimer = 0.3f;
        _airTime = 0f;
        _fallSpeed = 0f;
        if (_landTimer > 0f)
        {
            _landTimer -= dt;
            return "land";
        }
        if (ground < 0.3f) return "idle";
        if (_stride is Stride.StrafeLeft or Stride.StrafeRight && h.CanStrafe)
        {
            rate = h.StrafeRate(ground);
            return _stride == Stride.StrafeLeft ? "strafe_left" : "strafe_right";
        }
        string gait = h.Gait(ground, out rate);
        if (_stride == Stride.Backward) rate = -rate;
        return gait;
    }

    /// <summary>
    /// The upper-body action for using an item: its use style's clip (data/config/use_styles.json), the tool clip
    /// for tools, "use" for a style without one.
    /// </summary>
    public static string ActionFor(GameWorld game, ItemUseInfo info) => game.Styles.ClipFor(info);
}
