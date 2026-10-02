using Godot;
using Strata.Core.Content;
using Strata.Core.Content.Defs;
using Strata.Core.Gameplay.Items;
using Strata.Core.Session;
using Strata.Game.Items;
using Strata.Game.Rendering;

namespace Strata.Game.Player;

/// <summary>
/// The held item in first person (the body is hidden there): drawn at the lower right of the view, bobbing with
/// walking and animated by the use, timed like the body's clips (data/config/use_styles.json): swords and tools are
/// raised over the shoulder through the wind-up, sweep across in the strike window and follow through, thrust weapons
/// pull back and stab forward, everything else lifts, so every attack and dig is visible. The use's phase runs in real
/// time between simulation ticks, the item eases out of and back to its rest pose, and a chained use carries on from
/// the pose the last one ended in. Ranged weapons are held aimed (ViewModel.Ranged): a bow upright with an arrow on the
/// string, guns along the view at the crosshair with a recoil kick per shot, staffs tipped toward the crosshair. Lit by
/// the voxel light around the player. Kept in step with the held item in either view, so switching to first person
/// never loads a model.
/// </summary>
public sealed partial class ViewModel : Node3D
{
    private static readonly Vector3 Rest = new(0.32f, -0.3f, -0.55f);

    /// <summary>A pose of the held item: offset from <see cref="Rest"/> and rotation (Euler, radians).</summary>
    private readonly record struct Pose(Vector3 Offset, Vector3 Rotation)
    {
        public static Pose Lerp(Pose a, Pose b, float t) => new(a.Offset.Lerp(b.Offset, t), a.Rotation.Lerp(b.Rotation, t));
    }

    private static readonly Pose Idle = new(Vector3.Zero, new Vector3(-0.35f, 0.25f, 0.15f));

    // Swords and tools: every use starts and ends half raised (so chained swings flow), is raised over the right
    // shoulder by the end of the wind-up, comes down and across the view, then carries on (a blade) or bounces back
    // off the block (a tool).
    private static readonly Pose Poised = new(new Vector3(0.03f, 0.06f, -0.03f), new Vector3(0.3f, 0.4f, -0.15f));
    private static readonly Pose Raised = new(new Vector3(0.05f, 0.12f, -0.05f), new Vector3(0.9f, 0.5f, -0.4f));
    private static readonly Pose Struck = new(new Vector3(-0.2f, -0.08f, -0.05f), new Vector3(-1.6f, -0.3f, 0.6f));
    private static readonly Pose Carried = new(new Vector3(-0.24f, -0.11f, -0.04f), new Vector3(-1.78f, -0.38f, 0.68f));
    private static readonly Pose Bounced = Pose.Lerp(Struck, Raised, 0.15f);

    // Thrust weapons: level, pulled back through the wind-up, lunging forward through the strike window.
    private static readonly Pose Level = new(Vector3.Zero, new Vector3(-1.45f, 0.1f, 0f));
    private static readonly Pose Couched = new(new Vector3(0.02f, -0.01f, 0.07f), new Vector3(-1.45f, 0.1f, 0f));
    private static readonly Pose Lunge = new(new Vector3(-0.12f, 0.05f, -0.35f), new Vector3(-1.45f, 0.1f, 0f));

    /// <summary>Seconds to ease from rest into a use, back to rest after the last one, and from one use into the next.</summary>
    private const float EngageIn = 0.06f, EngageOut = 0.15f, ChainFade = 0.05f;

    private PlayerController _controller = null!;
    private GameWorld _game = null!;
    private PropLibrary _props = null!;
    private Node3D? _model;
    private GeometryInstance3D? _geometry;
    private ItemDef? _item;
    private float _bob;

    // The held item's use style (PlayerAvatar.ActionFor) and the pose it rests in between uses.
    private string _style = "use";
    private bool _ranged;
    private Pose _rest = Idle;

    // The use being shown: its serial and action, its phase (never runs backwards), how far the item is drawn out of
    // its rest pose, and the crossfade from the pose shown when a chained use began.
    private int _serial;
    private string _action = "use";
    private float _phase, _engage, _chain = 1f;
    private Pose _shown, _chainFrom;

    public void Initialize(PlayerController controller, GameWorld game, PropLibrary props)
    {
        _controller = controller;
        _game = game;
        _props = props;
        Name = "ViewModel";
        _game.ItemUse.Used += OnUsed;
    }

    public override void _ExitTree()
    {
        if (_game != null) _game.ItemUse.Used -= OnUsed;
    }

    public override void _Process(double delta)
    {
        using var profile = Strata.Core.Diagnostics.Metrics.Frame.Time("gd.view_model");
        var entity = _controller.Entity;
        float dt = (float)delta;
        UpdateItem(entity.State.Inventory.Held.Item);
        RefreshAmmo(entity, dt);
        Visible = _controller.Rig.FirstPerson && entity.Alive;
        if (!Visible)
        {
            // Out of view: the next time it shows, a use in progress is picked up at its phase and eased into.
            _serial = int.MinValue;
            _engage = 0f;
            _chain = 1f;
            _kick = 0f;
            return;
        }
        if (_model == null) return;

        var v = _controller.Velocity;
        float speed = new Vector2(v.X, v.Z).Length();
        _bob += dt * speed * 1.6f;
        var bob = _controller.IsOnFloor() ? new Vector3(Mathf.Sin(_bob) * 0.012f, Mathf.Abs(Mathf.Cos(_bob)) * 0.016f, 0) * Mathf.Min(speed / 5f, 1.2f) : Vector3.Zero;

        var pose = Animate(entity, dt);
        if (_ranged)
        {
            PlaceAimed(entity, pose, bob, dt);
            UpdateArrow();
        }
        else
        {
            Position = Rest + pose.Offset + bob;
            Rotation = pose.Rotation;
        }
        var light = PlayerLight.Brighten(LightProbe.SampleSmooth(_game.Session.World, _controller.GlobalPosition + Vector3.Up * 1.4f), entity);
        _geometry?.SetInstanceShaderParameter("voxel_light", light);
        _arrow?.SetInstanceShaderParameter("voxel_light", light);
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

    /// <summary>The held item's pose for an action (the body's clip: swing, mine, rapier, …) at use phase <paramref name="t"/>.</summary>
    private Pose UsePose(string action, float t)
    {
        var timing = _game.ItemUse.Timing;
        switch (action)
        {
            case Throw or Whip:
                return UsePose(UseTiming.Swing, t); // flung or lashed over the shoulder like a swing
            case UseTiming.Swing or UseTiming.Mine:
            {
                var clip = timing.Clip(action);
                float windup = clip.StrikeStart ?? 0f, end = clip.StrikeEnd ?? 1f, through = Mathf.Min(1f, end + UseTiming.FollowThrough);
                var after = action == UseTiming.Mine ? Bounced : Carried;
                if (t < windup) return Pose.Lerp(Poised, Raised, Ease(t / windup));
                if (t < end) return Pose.Lerp(Raised, Struck, timing.Sweep(action, t));
                if (t < through || through >= 1f) return Pose.Lerp(Struck, after, Ease((t - end) / Mathf.Max(1e-3f, through - end)));
                return Pose.Lerp(after, Poised, Ease((t - through) / (1f - through)));
            }
            case UseTiming.Rapier or UseTiming.Spear:
            {
                // Out at full length while the stab is live (its strike window and follow-through), then back to level.
                var clip = timing.Clip(action);
                float windup = clip.StrikeStart ?? 0f, end = clip.StrikeEnd ?? 1f, through = Mathf.Min(1f, end + UseTiming.FollowThrough);
                if (t < windup) return Pose.Lerp(Level, Couched, Ease(t / windup));
                if (t < end) return Pose.Lerp(Couched, Lunge, timing.Sweep(action, t));
                if (timing.Striking(action, t) || through >= 1f) return Lunge;
                return Pose.Lerp(Lunge, Level, Ease((t - through) / (1f - through)));
            }
            case UseTiming.Bow or Gun or Staff or Tome:
                return RangedPose(action, t);
            default:
            {
                float lift = Mathf.Sin(t * Mathf.Pi);
                return new Pose(new Vector3(0f, 0.08f * lift, -0.08f * lift), Idle.Rotation);
            }
        }
    }

    private static float Ease(float x) => Mathf.SmoothStep(0f, 1f, x);

    private void UpdateItem(ItemDef? item)
    {
        if (item == _item) return;
        _item = item;
        _model?.QueueFree();
        _model = null;
        _geometry = null;
        _arrow = null;
        LightAnchor = null;
        // A new item starts from its own rest pose: no crossfade from another item's.
        _serial = int.MinValue;
        _engage = 0f;
        _chain = 1f;
        _kick = 0f;
        _style = item == null ? "use" : PlayerAvatar.ActionFor(_game, _game.Uses[item]);
        _ranged = _style is UseTiming.Bow or Gun or Staff or Tome;
        _rest = _ranged ? RangedRest(_style) : Idle;
        _ammoClock = 0f; // look for arrows now: the nocked arrow is ready before the first shot
        if (item == null) return;
        var path = AssetPaths.ItemModel(item, _game.Session.Content);
        if (path != null && _props.Model(path, multimesh: false) is { } mesh)
        {
            var mi = new MeshInstance3D { Mesh = mesh, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            // Items are modelled grip at the origin, business end along +Y; placeables are shrunk to hand size.
            mi.Scale = Vector3.One * (item.PlaceObject == null ? 0.9f : item.HeldLight != null ? 0.8f : 0.3f);
            _geometry = mi;
            _model = mi;
            if (_style == UseTiming.Bow) AddArrow(mi);
        }
        else
        {
            _model = new Sprite3D { Texture = ItemIcons.Get(item), PixelSize = 0.18f / 128f, Shaded = false };
        }
        AddChild(_model);
        LightAnchor = _model;
        if (Flames.Of(item, _game.Session.Content) is { } flame)
        {
            var fire = Flames.Single(flame, embers: false); // sparks this close to the eye would look like snowflakes
            _model.AddChild(fire);
            LightAnchor = fire;
        }
    }

    /// <summary>Where a carried light shines from in first person: the held torch's flame, or the held item.</summary>
    public Node3D? LightAnchor { get; private set; }
}
