using Godot;
using Strata.Core.Content.Defs;
using Strata.Core.Gameplay.Items;
using Strata.Core.Session;
using Strata.Game.Rendering;

namespace Strata.Game.Player;

/// <summary>
/// How the body moves: the legs face where the player walks (backpedalling when walking away from the aim) while the
/// torso and head turn toward the aim; locomotion follows the controller (walk, run, sprint, jump, fall, land, swim,
/// climb, glide); every item use plays its action over the upper body, stretched to the item's use time; a carried
/// torch is held up; hits make the body flinch.
/// </summary>
public sealed partial class PlayerAvatar
{
    private static readonly float MaxTwist = Mathf.DegToRad(70f);
    private static readonly float Backpedal = Mathf.DegToRad(110f);
    private float _legYaw, _airTime, _fallSpeed, _landTimer;
    private int _lastUse, _lastHurt = int.MaxValue;
    private bool _backwards;

    private void Animate(PlayerEntity entity, float dt)
    {
        var aim = entity.Aim;
        float aimYaw = Mathf.Atan2(-aim.X, -aim.Z);
        var h = _visual!.Humanoid;
        if (h == null)
        {
            _legYaw = Mathf.LerpAngle(_legYaw, aimYaw, 1f - Mathf.Exp(-dt * 14f));
            Rotation = new Vector3(0, _legYaw, 0);
            _visual.Play(ChooseClip(entity, out float speed), speed: speed);
            return;
        }
        var v = _controller.Velocity;
        float ground = new Vector2(v.X, v.Z).Length();
        bool upright = _controller.Swimming || _controller.Climbing || !_controller.IsOnFloor();
        TurnLegs(aimYaw, v, ground, upright, entity.Alive, dt);
        Rotation = new Vector3(0, _legYaw, 0);

        string state = Locomotion(entity, h, v, ground, dt, out float rate);
        h.Locomotion(state, rate);

        // Item actions: a new use (the use timer jumps back up) starts the item's action.
        int use = entity.UseAnimation;
        if (use > _lastUse && _heldItem != null && entity.Alive)
            h.Play(ActionFor(_heldItem, _game.Uses[_heldItem]), use / (float)Strata.Core.Common.Units.TicksPerSecond);
        _lastUse = use;
        int sinceHurt = entity.State.Vitals.TicksSinceHurt;
        if (sinceHurt < _lastHurt && entity.Alive) h.Hurt();
        _lastHurt = sinceHurt;
        h.Hold(_heldItem?.HeldLight != null && h.Action == null && entity.Alive);

        if (h.Aim is { } aimMod)
        {
            aimMod.Twist = entity.Alive ? Mathf.AngleDifference(_legYaw, aimYaw) : 0f;
            aimMod.Pitch = entity.Alive ? Mathf.Atan2(aim.Y, new Vector2(aim.X, aim.Z).Length()) : 0f;
            aimMod.ArmAim = h.Action is "shoot" or "bow" or "cast" or "thrust" ? 1f : h.Action != null ? 0.5f : 0.15f;
        }
    }

    /// <summary>
    /// The legs point where the body moves, turned toward the aim by at most <see cref="MaxTwist"/> (the torso makes
    /// up the rest); moving away from the aim they face it and walk backwards. Standing still, the feet stay put until
    /// the aim swings too far round. In the air, water or on a rope the whole body faces the aim.
    /// </summary>
    private void TurnLegs(float aimYaw, Vector3 v, float ground, bool upright, bool alive, float dt)
    {
        float target = _legYaw;
        _backwards = false;
        if (!alive) target = _legYaw;
        else if (upright) target = aimYaw;
        else if (ground > 0.6f)
        {
            float move = Mathf.Atan2(-v.X, -v.Z);
            float diff = Mathf.AngleDifference(aimYaw, move);
            if (Mathf.Abs(diff) > Backpedal)
            {
                _backwards = true;
                diff = Mathf.AngleDifference(aimYaw, move + Mathf.Pi);
            }
            target = aimYaw + Mathf.Clamp(diff, -MaxTwist, MaxTwist);
        }
        else if (Mathf.Abs(Mathf.AngleDifference(_legYaw, aimYaw)) > Mathf.DegToRad(55f)) target = aimYaw;
        _legYaw = Mathf.LerpAngle(_legYaw, target, 1f - Mathf.Exp(-dt * 10f));
    }

    private string Locomotion(PlayerEntity entity, HumanoidAnimator h, Vector3 v, float ground, float dt, out float rate)
    {
        rate = 1f;
        if (!entity.Alive) return "death";
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
        string gait = h.Gait(ground, out rate);
        if (_backwards) rate = -rate;
        return gait;
    }

    /// <summary>The upper-body action for using an item (Terraria's use styles, told apart by what the item does).</summary>
    public static string ActionFor(ItemDef item, ItemUseInfo info)
    {
        if (info.Has(ItemAction.Consume) || item.UseStyle == UseStyle.Consume) return "drink";
        if (info.IsTool) return "mine";
        if (info.Has(ItemAction.Thrust) || item.UseStyle == UseStyle.Thrust) return "thrust";
        if (info.Has(ItemAction.Melee) || info.Has(ItemAction.Fish) || item.UseStyle == UseStyle.Swing) return "swing";
        if (item.DamageType is DamageClass.Magic or DamageClass.Summon || item.ManaCost > 0) return "cast";
        if (info.Has(ItemAction.Shoot) || item.UseStyle == UseStyle.Shoot) return item.UseAmmo == "arrow" ? "bow" : "shoot";
        return "use";
    }
}
