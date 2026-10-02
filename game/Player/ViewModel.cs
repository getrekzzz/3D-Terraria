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
/// walking and posed per use style from data/config/use_styles.json <c>fp</c> (ViewModel.Poses): swords sweep the
/// gameplay swing curve and thrusts its extension (ViewModel.Swing), tools chop, potions and food are raised to the
/// mouth, and ranged weapons are held aimed at the crosshair (ViewModel.Ranged) with a recoil kick per shot. Lit by
/// the voxel light around the player. Kept in step with the held item in either view, so switching to first person
/// never loads a model.
/// </summary>
public sealed partial class ViewModel : Node3D
{
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
    private Pose _rest;

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
        _game.ItemUse.Shot += OnShot; // recoil per shot (ViewModel.Poses)
    }

    public override void _ExitTree()
    {
        if (_game != null) _game.ItemUse.Shot -= OnShot;
    }

    public override void _Process(double delta)
    {
        using var profile = Strata.Core.Diagnostics.Metrics.Frame.Time("gd.view_model");
        var entity = _controller.Entity;
        float dt = (float)delta;
        UpdateItem(entity.State.Inventory.Held.Item);
        RefreshAmmo(entity, dt);
        Visible = _controller.Rig.FirstPerson && entity.Alive && !entity.Sleeping; // nothing in hand in bed (P3-34)
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
            PlaceHeld(pose, bob, dt);
        }
        var light = PlayerLight.Brighten(LightProbe.SampleSmooth(_game.Session.World, _controller.GlobalPosition + Vector3.Up * 1.4f), entity);
        _geometry?.SetInstanceShaderParameter("voxel_light", light);
        _arrow?.SetInstanceShaderParameter("voxel_light", light);
    }

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
        LoadPoses(item); // the style's first-person poses (ViewModel.Poses)
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
            _model = FallbackModel(item); // a block's textured cube (ViewModel.Block), else the icon
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
