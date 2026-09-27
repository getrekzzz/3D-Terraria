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
/// walking and animated by the use — swords and tools sweep across, thrust weapons stab forward, everything else
/// lifts — so every attack and dig is visible. Lit by the voxel light around the player.
/// </summary>
public sealed partial class ViewModel : Node3D
{
    private static readonly Vector3 Rest = new(0.32f, -0.3f, -0.55f);

    private PlayerController _controller = null!;
    private GameWorld _game = null!;
    private PropLibrary _props = null!;
    private Node3D? _model;
    private GeometryInstance3D? _geometry;
    private ItemDef? _item;
    private float _bob;

    public void Initialize(PlayerController controller, GameWorld game, PropLibrary props)
    {
        _controller = controller;
        _game = game;
        _props = props;
        Name = "ViewModel";
    }

    public override void _Process(double delta)
    {
        using var profile = Strata.Core.Diagnostics.Metrics.Frame.Time("gd.view_model");
        var entity = _controller.Entity;
        Visible = _controller.Rig.FirstPerson && entity.Alive;
        if (!Visible) return;
        UpdateItem(entity.State.Inventory.Held.Item);
        if (_model == null) return;

        var v = _controller.Velocity;
        float speed = new Vector2(v.X, v.Z).Length();
        _bob += (float)delta * speed * 1.6f;
        var bob = _controller.IsOnFloor() ? new Vector3(Mathf.Sin(_bob) * 0.012f, Mathf.Abs(Mathf.Cos(_bob)) * 0.016f, 0) * Mathf.Min(speed / 5f, 1.2f) : Vector3.Zero;

        var info = _game.Uses[_item!];
        float t = entity.UseAnimation > 0 ? 1f - entity.UseAnimation / (float)Mathf.Max(1, _item!.UseAnimation) : 0f;
        var position = Rest + bob;
        var rotation = new Vector3(-0.35f, 0.25f, 0.15f);
        if (t > 0)
        {
            if (info.Has(ItemAction.Thrust) && !info.Has(ItemAction.Melee))
            {
                float stab = Mathf.Sin(t * Mathf.Pi);
                position += new Vector3(-0.12f * stab, 0.05f * stab, -0.35f * stab);
                rotation = new Vector3(-1.45f, 0.1f, 0f);
            }
            else if (info.Has(ItemAction.Melee) || info.IsTool)
            {
                // Raised over the right shoulder, then brought down and across the view.
                float s = Mathf.SmoothStep(0f, 1f, t);
                rotation = new Vector3(Mathf.Lerp(0.9f, -1.6f, s), Mathf.Lerp(0.5f, -0.3f, s), Mathf.Lerp(-0.4f, 0.6f, s));
                position += new Vector3(Mathf.Lerp(0.05f, -0.2f, s), Mathf.Lerp(0.12f, -0.08f, s), -0.05f);
            }
            else
            {
                position += new Vector3(0, 0.08f * Mathf.Sin(t * Mathf.Pi), -0.08f * Mathf.Sin(t * Mathf.Pi));
            }
        }
        Position = position;
        Rotation = rotation;
        var light = PlayerLight.Brighten(LightProbe.SampleSmooth(_game.Session.World, _controller.GlobalPosition + Vector3.Up * 1.4f), entity);
        _geometry?.SetInstanceShaderParameter("voxel_light", light);
    }

    private void UpdateItem(ItemDef? item)
    {
        if (item == _item) return;
        _item = item;
        _model?.QueueFree();
        _model = null;
        _geometry = null;
        LightAnchor = null;
        if (item == null) return;
        var path = AssetPaths.ItemModel(item, _game.Session.Content);
        if (path != null && _props.Model(path, multimesh: false) is { } mesh)
        {
            var mi = new MeshInstance3D { Mesh = mesh, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            // Items are modelled grip at the origin, business end along +Y; placeables are shrunk to hand size.
            mi.Scale = Vector3.One * (item.PlaceObject == null ? 0.9f : item.HeldLight != null ? 0.8f : 0.3f);
            _geometry = mi;
            _model = mi;
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
