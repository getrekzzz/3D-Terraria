using Godot;
using Strata.Core.Content;
using Strata.Core.Content.Defs;
using Strata.Core.Gameplay.Items;
using Strata.Core.Session;
using Strata.Game.Items;
using Strata.Game.Rendering;

namespace Strata.Game.Player;

/// <summary>
/// The visible player body in third person: the animated human (see PlayerAvatar.Motion), the held item in the right
/// hand, worn armour (PlayerAvatar.Armor) and voxel lighting.
/// </summary>
public sealed partial class PlayerAvatar : Node3D
{
    private PlayerController _controller = null!;
    private GameWorld _game = null!;
    private PropLibrary _props = null!;
    private CreatureVisual? _visual;
    private Node3D? _held;
    private ItemDef? _heldItem;

    public void Initialize(PlayerController controller, GameWorld game, PropLibrary props)
    {
        _controller = controller;
        _game = game;
        _props = props;
        Name = "Avatar";
        InitializeGear();
        InitializeArmor();
        var look = controller.Entity.State.Look;
        var model = $"assets/models/creatures/{look.ModelId}.glb";
        _visual = CreatureVisual.Create(ResourceLoader.Exists("res://" + model) ? model : "assets/models/creatures/player.glb", props, aim: true, now: true);
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
        UpdateHeld(entity.Minecart != null ? default : entity.State.Inventory.Held);
        UpdateCart(entity.Minecart);
        UpdateMount(entity.Mount);
        UpdateGear(delta);
        Animate(entity, (float)delta);
        _visual.SetLight(PlayerLight.Brighten(LightProbe.SampleSmooth(_game.Session.World, GlobalPosition + Vector3.Up * 0.9f), _controller.Entity));
        UpdateArmor((float)delta);
    }

    /// <summary>Clip choice for bodies without an animation set (their own embedded clips).</summary>
    private string ChooseClip(PlayerEntity entity, out float speed)
    {
        speed = 1f;
        if (!entity.Alive) return "death";
        if (entity.UseAnimation > 0 && _heldItem != null)
        {
            var info = _game.Uses[_heldItem];
            return info.Has(ItemAction.Melee) || info.IsTool || _heldItem.UseStyle.Family() == UseStyle.Swing ? "swing" : "use";
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
        float scale = 1f;
        if (path != null && _props.Model(path, multimesh: false) is { } mesh)
        {
            node = new MeshInstance3D { Mesh = mesh };
            scale = HeldScale(item);
        }
        else
        {
            node = new Sprite3D { Texture = ItemIcons.Get(item), PixelSize = 0.25f / 128f, Shaded = true };
        }
        node.Transform = HeldPose(item, _game.Uses[item], scale);
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

    /// <summary>
    /// The held item in the hand: how far (degrees) its limbs or shaft (+Y) lean from straight up, and how far its
    /// business end (a bow's back, −X; anything else's +Y) points from the aim. −1 with nothing in hand.
    /// </summary>
    public (float Tilt, float Aim) HeldDegrees()
    {
        if (_held == null || _heldItem == null || !IsInstanceValid(_held) || !_held.IsInsideTree()) return (-1f, -1f);
        var b = _held.GlobalBasis.Orthonormalized();
        var a = _controller.Entity.Aim;
        var axis = ActionFor(_heldItem, _game.Uses[_heldItem]) == UseTiming.Bow ? -b.X : b.Y;
        return (Mathf.RadToDeg(b.Y.AngleTo(Vector3.Up)), Mathf.RadToDeg(axis.AngleTo(new Vector3(a.X, a.Y, a.Z))));
    }

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

    /// <summary>The hand bone's fist: where a held item's grip sits (m along the bone, past the wrist).</summary>
    private const float Fist = 0.08f;

    /// <summary>How far a bow's riser (its grip) lies from the model's origin on the string, toward −X (bgen items.bow).</summary>
    private const float BowGrip = 0.17f;

    /// <summary>
    /// The bow clip holds the bow hand about 23° above the aim (the arm stays on the aim pitch), so the bow is set that
    /// much forward in the fist to stand upright with its back on the aim.
    /// </summary>
    private static readonly float BowSet = Mathf.DegToRad(23f);

    /// <summary>
    /// How an item sits in the right fist (the hand bone: +Y along the fingers, +Z the thumb side). Items are modelled
    /// with the grip at the origin and the business end along +Y, which points out of the thumb side: a sword is carried
    /// point-forward and lines up with the arm when the wrist cocks for a strike, a gun with the arm for a shot. A bow
    /// (limbs along ±Y, its back toward −X, the string on the origin) is gripped by its riser and stands upright with its
    /// back toward the target when the bow arm is held out: limbs along the thumb side, back along the fingers.
    /// </summary>
    private static Transform3D HeldPose(ItemDef item, ItemUseInfo info, float scale)
    {
        if (ActionFor(item, info) == UseTiming.Bow)
        {
            var upright = new Basis(Vector3.Right, -BowSet) * new Basis(new Vector3(0, -1, 0), new Vector3(0, 0, 1), new Vector3(-1, 0, 0));
            var riser = upright * new Vector3(-BowGrip * scale, 0, 0);
            return new Transform3D(upright.Scaled(Vector3.One * scale), new Vector3(0, Fist, 0) - riser);
        }
        return new Transform3D(Basis.FromEuler(new Vector3(Mathf.Pi / 2f, 0, 0)).Scaled(Vector3.One * scale), new Vector3(0, Fist, 0));
    }

    /// <summary>
    /// Placeable furniture shrinks to a hand-held size; lights (torches, candles) are carried at nearly full size so
    /// they read in the hand; tools and weapons keep their modelled size.
    /// </summary>
    private static float HeldScale(ItemDef item) =>
        item.PlaceObject == null ? 1f : item.HeldLight != null ? 0.95f : 0.3f;
}
