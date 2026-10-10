using Godot;
using Strata.Core.Content.Defs;
using Strata.Core.Gameplay.Entities;
using Strata.Core.Gameplay.Interaction;
using Strata.Core.Gameplay.Items;
using Strata.Core.Presentation;
using Strata.Core.Session;

namespace Strata.Game.Player;

/// <summary>
/// First-person poses per use style (P3-07), from data/config/use_styles.json <c>fp</c>: each style's rest pose, its
/// keyed use (relative to the rest, sampled at the use's phase), or one of the shared gameplay curves (the swing's
/// blade angle, a thrust's extension; ViewModel.Swing) so the arc on screen is the arc that hits. Styles marked
/// <c>aimAtCrosshair</c> are turned onto the crosshair point (ViewModel.Ranged); <c>raiseToFace</c> uses are kept clear
/// of the near plane; every shot (a burst's included) kicks by the style's <c>recoil</c>, decaying over its time. The
/// use's phase runs in real time between ticks, the item eases out of and back to its rest pose, and a chained use
/// carries on from the pose the last one ended in.
/// </summary>
public sealed partial class ViewModel
{
    /// <summary>The view model's anchor in view space (lower right); poses are offsets from it.</summary>
    private static readonly Vector3 Rest = new(0.32f, -0.3f, -0.55f);

    /// <summary>A pose of the held item: offset from <see cref="Rest"/> and rotation (Euler, radians, Godot's YXZ).</summary>
    private readonly record struct Pose(Vector3 Offset, Vector3 Rotation)
    {
        public static Pose Lerp(Pose a, Pose b, float t) => new(a.Offset.Lerp(b.Offset, t), a.Rotation.Lerp(b.Rotation, t));

        public static Pose operator +(Pose a, Pose b) => new(a.Offset + b.Offset, a.Rotation + b.Rotation);

        public static Pose operator *(Pose a, float k) => new(a.Offset * k, a.Rotation * k);
    }

    /// <summary>The rest pose of a style without an fp entry.</summary>
    private static readonly Pose Idle = new(Vector3.Zero, new Vector3(-0.35f, 0.25f, 0.15f));

    /// <summary>Seconds to ease from rest into a use, back to rest after the last one, and from one use into the next.</summary>
    private const float EngageIn = 0.06f, EngageOut = 0.15f, ChainFade = 0.05f;

    /// <summary>The nearest (m in front of the eye) a raised-to-the-face item comes, clear of the near plane at any FOV.</summary>
    private const float FaceDepth = 0.24f;

    private readonly record struct Key(float T, Pose Pose, CurveEase Ease);

    // The held item's first-person style: its keys (tool keys for tools), curve, flags and recoil.
    private Key[] _keys = System.Array.Empty<Key>();
    private string? _curve;
    private bool _toFace;
    private Pose _recoil;
    private float _recoilRate = 25f;
    private int _kicks;
    private ulong _kickTick = ulong.MaxValue;

    /// <summary>Shots that kicked the first-person item since start (probe <c>fp.kicks</c>).</summary>
    public int Kicks => _kicks;

    /// <summary>The current recoil weight (1 just after a shot, decaying; probe <c>fp.recoil</c>).</summary>
    public float RecoilWeight => _kick;

    /// <summary>The first-person style's name the held item poses by (probe <c>fp.style</c> as its index; −1 with nothing).</summary>
    public int StyleIndex { get; private set; } = -1;

    /// <summary>How far (m) in front of the eye the held item is (probe <c>fp.depth</c>); −1 out of view.</summary>
    public float Depth => Visible && _model != null ? -Position.Z : -1f;

    private static Vector3 Vec(float[] v, bool degrees) =>
        v.Length != 3 ? Vector3.Zero : degrees ? new Vector3(Mathf.DegToRad(v[0]), Mathf.DegToRad(v[1]), Mathf.DegToRad(v[2])) : new Vector3(v[0], v[1], v[2]);

    private static Pose PoseOf(FpPose p) => new(Vec(p.Pos, false), Vec(p.Rot, true));

    /// <summary>Reads the held item's first-person style (UpdateItem).</summary>
    private void LoadPoses(ItemDef? item)
    {
        FpStyle? fp = null;
        bool tool = false;
        _kicks = 0; // counted per item (probe fp.kicks)
        StyleIndex = -1;
        if (item != null)
        {
            var info = _game.Uses[item];
            fp = _game.Styles.Style(info.Style).Fp;
            tool = info.IsTool;
            StyleIndex = (int)info.Style;
        }
        _rest = fp == null ? Idle : PoseOf(fp.Rest);
        _curve = fp?.Curve;
        if (tool && _curve == "blade") _curve = null; // a pickaxe of the swing style chops (its tool keys)
        _ranged = fp?.AimAtCrosshair == true;
        _toFace = fp?.RaiseToFace == true;
        _recoil = fp?.Recoil is { } r ? PoseOf(r) : default;
        _recoilRate = fp?.Recoil is { } rr ? Mathf.Log(20f) / rr.Decay : 25f; // ~5 % left after the decay time
        var keys = (tool ? fp?.ToolKeys : null) ?? fp?.Keys;
        _keys = keys == null ? System.Array.Empty<Key>() : System.Array.ConvertAll(keys, k => new Key(k.T, PoseOf(k), SwingCurve.EaseNamed(k.Ease)));
    }

    /// <summary>
    /// The pose this frame: the use's pose at its phase (sub-tick: the physics interpolation fraction is added, so it
    /// moves every frame at any frame rate), drawn out of the rest pose by the engage weight, crossfaded from the last
    /// pose when a chained use starts.
    /// </summary>
    private Pose Animate(PlayerEntity entity, float dt)
    {
        bool active = entity.UseAnimation > 0 && entity.UseItem == _item;
        if (active && entity.UseSerial != _serial)
        {
            _serial = entity.UseSerial;
            _action = PlayerAvatar.ActionFor(_game, _game.Uses[_item!]);
            _phase = 0f;
            if (_engage > 0f)
            {
                _chainFrom = _shown;
                _chain = 0f;
            }
        }
        if (active)
        {
            float frac = _controller.Runtime.Simulating ? (float)Engine.GetPhysicsInterpolationFraction() : 0f;
            int max = Mathf.Max(1, entity.UseAnimationMax);
            _phase = Mathf.Max(_phase, Mathf.Clamp((max - entity.UseAnimation + frac) / max, 0f, 1f));
        }
        else if (_engage > 0f)
        {
            _phase = 1f; // the use played out: hold its end pose while easing back to rest
        }
        _engage = Mathf.MoveToward(_engage, active ? 1f : 0f, dt / (active ? EngageIn : EngageOut));
        _chain = Mathf.MoveToward(_chain, 1f, dt / ChainFade);

        var pose = _engage > 0f ? Pose.Lerp(_rest, UsePose(_action, _phase), Mathf.SmoothStep(0f, 1f, _engage)) : _rest;
        if (_chain < 1f) pose = Pose.Lerp(_chainFrom, pose, Mathf.SmoothStep(0f, 1f, _chain));
        _shown = pose;
        return pose;
    }

    /// <summary>The held item's pose at use phase <paramref name="t"/>: its style's curve, else its keys over the rest.</summary>
    private Pose UsePose(string action, float t)
    {
        if (_curve == "blade") return BladePose(t); // the shared swing curve (ViewModel.Swing)
        if (_curve == "extend") return ThrustPose(action is UseTiming.Rapier or UseTiming.Spear ? action : UseTiming.Spear, t);
        return _rest + KeyedPose(t);
    }

    /// <summary>The keyed offset from rest at phase <paramref name="t"/> (held at the first and last keys outside them).</summary>
    private Pose KeyedPose(float t)
    {
        var keys = _keys;
        if (keys.Length == 0) return default;
        if (t <= keys[0].T) return keys[0].Pose;
        for (int i = 1; i < keys.Length; i++)
        {
            var b = keys[i];
            if (t > b.T) continue;
            var a = keys[i - 1];
            float u = (t - a.T) / Mathf.Max(b.T - a.T, 1e-6f);
            return Pose.Lerp(a.Pose, b.Pose, SwingCurve.Ease(b.Ease, u));
        }
        return keys[^1].Pose;
    }

    /// <summary>Places a pose that is not aimed: at the anchor plus the bob, a lift to the face kept off the near plane.</summary>
    private void PlaceHeld(Pose pose, Vector3 bob, float dt)
    {
        _kick *= Mathf.Exp(-_recoilRate * dt);
        var at = Rest + pose.Offset + bob + _recoil.Offset * _kick;
        if (_toFace) at.Z = Mathf.Min(at.Z, -FaceDepth);
        Position = at;
        Rotation = pose.Rotation + _recoil.Rotation * _kick;
    }

    /// <summary>Every shot of the held item, a burst's included, kicks it by its style's recoil.</summary>
    private void OnShot(ShotRequest shot)
    {
        if (shot.Player != _controller.Entity || shot.Weapon.Item != _item || _recoil == default) return;
        _kick = 1f;
        ulong tick = Engine.GetPhysicsFrames();
        if (tick != _kickTick) _kicks++; // a shotgun's pellets are one shot
        _kickTick = tick;
    }

    private static float Ease(float x) => Mathf.SmoothStep(0f, 1f, x);
}
