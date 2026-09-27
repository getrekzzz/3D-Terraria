using Godot;
using Strata.Core.Content;
using Strata.Core.Content.Defs;
using Strata.Core.Gameplay.Items;
using Strata.Core.Session;
using Strata.Game.Items;
using Strata.Game.Rendering;

namespace Strata.Game.Player;

/// <summary>
/// The visible player body in third person: animated humanoid facing the aim, locomotion/use/death clips,
/// the held item in the right hand and voxel lighting.
/// </summary>
public sealed partial class PlayerAvatar : Node3D
{
    private PlayerController _controller = null!;
    private GameWorld _game = null!;
    private PropLibrary _props = null!;
    private CreatureVisual? _visual;
    private Node3D? _held;
    private ItemDef? _heldItem;
    private float _yaw;

    public void Initialize(PlayerController controller, GameWorld game, PropLibrary props)
    {
        _controller = controller;
        _game = game;
        _props = props;
        Name = "Avatar";
        InitializeGear();
        var look = controller.Entity.State.Look;
        var model = $"assets/models/creatures/{look.ModelId}.glb";
        _visual = CreatureVisual.Create(ResourceLoader.Exists("res://" + model) ? model : "assets/models/creatures/player.glb", props);
        if (_visual == null) return;
        AddChild(_visual);
        if (ResourceLoader.Exists("res://" + model)) _visual.SetAppearance(look);
        // Full detail up close: simplified LODs let the hair shell sink into the scalp.
        _visual.SetLodBias(8f);
    }

    public override void _Process(double delta)
    {
        using var profile = Strata.Core.Diagnostics.Metrics.Frame.Time("gd.avatar");
        if (_visual == null) return;
        var entity = _controller.Entity;
        int immune = entity.State.Vitals.ImmunityTicks;
        Visible = !_controller.Rig.FirstPerson && (immune <= 0 || (immune / 4) % 2 == 0); // blink while invulnerable
        var aim = entity.Aim;
        float target = Mathf.Atan2(-aim.X, -aim.Z);
        _yaw = Mathf.LerpAngle(_yaw, target, 1f - Mathf.Exp(-(float)delta * 14f));
        Rotation = new Vector3(0, _yaw, 0);
        UpdateHeld(entity.Minecart != null ? default : entity.State.Inventory.Held);
        UpdateCart(entity.Minecart);
        UpdateGear(delta);
        _visual.Play(ChooseClip(entity, out float speed), speed: speed);
        _visual.SetLight(PlayerLight.Brighten(LightProbe.SampleSmooth(_game.Session.World, GlobalPosition + Vector3.Up * 0.9f), _controller.Entity));
    }

    private string ChooseClip(PlayerEntity entity, out float speed)
    {
        speed = 1f;
        if (!entity.Alive) return "death";
        if (entity.UseAnimation > 0 && _heldItem != null)
        {
            var info = _game.Uses[_heldItem];
            return info.Has(ItemAction.Melee) || info.IsTool ? "swing" : "use";
        }
        var v = _controller.Velocity;
        if (!_controller.IsOnFloor() && !_controller.Frozen) return v.Y > 0 ? "jump" : "fall";
        float horizontal = new Vector2(v.X, v.Z).Length();
        if (horizontal > 5.6f)
        {
            speed = horizontal / 6.5f;
            return "run";
        }
        if (horizontal > 0.3f)
        {
            speed = Mathf.Max(0.6f, horizontal / 4.2f);
            return "walk";
        }
        return "idle";
    }

    private void UpdateHeld(ItemStack held)
    {
        var item = held.Item;
        if (item == _heldItem) return;
        _heldItem = item;
        _held?.QueueFree();
        _held = null;
        LightAnchor = null;
        if (item == null || _visual == null) return;
        var path = AssetPaths.ItemModel(item, _game.Session.Content);
        Node3D node;
        if (path != null && _props.Model(path, multimesh: false) is { } mesh)
        {
            node = new MeshInstance3D { Mesh = mesh, Scale = Vector3.One * HeldScale(item) };
        }
        else
        {
            node = new Sprite3D { Texture = ItemIcons.Get(item), PixelSize = 0.25f / 128f, Shaded = true };
        }
        // Items are modelled with the grip at the origin and the business end along +Y; point it forward.
        node.RotationDegrees = new Vector3(90, 0, 0);
        node.Position = new Vector3(0, 0.08f, 0);
        _held = node;
        LightAnchor = node;
        if (Flames.Of(item, _game.Session.Content) is { } flame)
        {
            var fire = Flames.Single(flame, embers: true);
            node.AddChild(fire);
            LightAnchor = fire;
        }
        if (!_visual.AttachToBone("hand.R", node))
        {
            node.QueueFree();
            LightAnchor = null;
        }
    }

    /// <summary>Where a carried light shines from in third person: the held torch's flame, or the held item.</summary>
    public Node3D? LightAnchor { get; private set; }

    private MeshInstance3D? _cart;

    /// <summary>The minecart under a riding player, facing along the track.</summary>
    private void UpdateCart(Strata.Core.Gameplay.Mounts.MinecartRide? ride)
    {
        if (ride == null)
        {
            if (_cart != null) _cart.Visible = false;
            return;
        }
        if (_cart == null)
        {
            _cart = new MeshInstance3D { Mesh = _props.Model("assets/models/items/minecart.glb", multimesh: false), TopLevel = true };
            AddChild(_cart);
        }
        _cart.Visible = true;
        var h = new Vector3(ride.Heading.X, 0, ride.Heading.Z);
        var basis = h.LengthSquared() > 1e-4f ? Basis.LookingAt(h.Normalized(), Vector3.Up) : Basis.Identity;
        _cart.GlobalTransform = new Transform3D(basis.Scaled(Vector3.One * 1.6f), _controller.GlobalPosition - Vector3.Up * 0.2f);
    }

    /// <summary>
    /// Placeable furniture shrinks to a hand-held size; lights (torches, candles) are carried at nearly full size so
    /// they read in the hand; tools and weapons keep their modelled size.
    /// </summary>
    private static float HeldScale(ItemDef item) =>
        item.PlaceObject == null ? 1f : item.HeldLight != null ? 0.95f : 0.3f;
}
