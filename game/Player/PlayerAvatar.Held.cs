using Godot;
using Strata.Core.Content;
using Strata.Core.Content.Defs;
using Strata.Core.Gameplay.Items;
using Strata.Game.Items;

namespace Strata.Game.Player;

/// <summary>
/// The held item's lighting and block cubes in third person (P3-06): the held node is attached to the hand as a lit
/// attachment of the body (<see cref="Rendering.CreatureVisual.AddLit"/>), so it darkens in caves and warms by torches
/// with the body; a held block is a small cube textured with the block's own terrain faces
/// (<see cref="BlockItemMeshes"/>) instead of a flat icon card. Sockets of the held model (muzzle, tip, string) are
/// exposed in world space for effects. Each use style grips the item its own way (<see cref="HeldPose"/>: the style's
/// hand frame from data/config/use_styles.json times the inverse of the model's grip socket).
/// </summary>
public sealed partial class PlayerAvatar
{
    private ItemDef? _socketItem;
    private string? _socketPath;

    /// <summary>Whether the held item's geometry takes the body's voxel light (probe <c>avatar.held_lit</c>).</summary>
    public bool HeldLit => _held is { } held && IsInstanceValid(held) && _visual != null && _visual.IsLit(HeldGeometry(held));

    /// <summary>Whether the held item is drawn as a textured block cube (probe <c>avatar.held_block_cube</c>).</summary>
    public bool HeldBlockCube => _held is { } held && IsInstanceValid(held) && HeldGeometry(held) is MeshInstance3D { Mesh: ArrayMesh } mi
        && mi.Mesh.SurfaceGetMaterial(0) == BlockItemMeshes.Material;

    /// <summary>
    /// The held item when it has no model: a block's cube (its near face in the fist, the cube along the item's +Y),
    /// else its icon as a card.
    /// </summary>
    private Node3D HeldFallback(ItemDef item)
    {
        if (BlockItemMeshes.ForItem(item, _game.Session.Content, _controller.Runtime.Services.Render) is { } cube)
        {
            var holder = new Node3D { Name = "BlockItem" };
            holder.AddChild(new MeshInstance3D { Mesh = cube, Position = new Vector3(0f, BlockItemMeshes.Size * 0.3f, 0f), LodBias = 8f });
            return holder;
        }
        return new Sprite3D { Texture = ItemIcons.Get(item), PixelSize = 0.25f / 128f, Shaded = true };
    }

    /// <summary>Attaches the held node to the right hand, lit with the body.</summary>
    private bool AttachHeld(Node3D node) => _visual!.AttachToBone("hand.R", node, lit: true);

    /// <summary>Drops the held node (and its lighting).</summary>
    private void ReleaseHeld()
    {
        if (_held == null) return;
        _visual?.Detach(_held);
        _held.QueueFree();
    }

    /// <summary>The geometry that shows the held item: the node itself, or a block cube's mesh under its holder.</summary>
    private static Node3D HeldGeometry(Node3D held) => held is GeometryInstance3D || held.GetChildCount() == 0 ? held : held.GetChild<Node3D>(0);

    /// <summary>
    /// A socket of the held model (grip, muzzle, tip, string, …) in world space; false with nothing in hand, a model
    /// without that socket, or the avatar hidden from the tree.
    /// </summary>
    public bool TrySocket(string name, out Transform3D global)
    {
        global = default;
        if (_held == null || _heldItem == null || !IsInstanceValid(_held) || !_held.IsInsideTree()) return false;
        if (_socketItem != _heldItem)
        {
            _socketItem = _heldItem;
            _socketPath = AssetPaths.ItemModel(_heldItem, _game.Session.Content);
        }
        if (_socketPath == null || _props.Socket(_socketPath, name) is not { } local) return false;
        global = _held.GlobalTransform * local;
        return true;
    }

    /// <summary>The hand bone's fist: where a held item's grip sits by default (m along the bone, past the wrist).</summary>
    private const float Fist = 0.08f;

    /// <summary>
    /// How an item sits in the right fist (the hand bone: +Y along the fingers, +Z the thumb side): the style's hand
    /// frame (data/config/use_styles.json <c>hand</c>; the default carries the business end, the model's +Y, out of
    /// the thumb side) times the inverse of the model's grip socket, so the socket lands on the frame whatever the
    /// model's origin (a bow is gripped by its riser, a gun by its handle). Items are modelled business end along +Y,
    /// so a grip socket at the origin pointing +Y leaves the model as it is.
    /// </summary>
    private Transform3D HeldPose(ItemUseInfo info, string? path, float scale)
    {
        var hand = _game.Styles.Style(info.Style).Hand;
        var frame = HandFrame(hand);
        frame = new Transform3D(frame.Basis.Scaled(Vector3.One * scale), frame.Origin);
        return frame * GripInverse(path);
    }

    /// <summary>A style's hand frame: its offset (m) and rotation (degrees, XYZ: R = Rx·Ry·Rz) in the hand bone.</summary>
    public static Transform3D HandFrame(Strata.Core.Presentation.HandGrip? hand)
    {
        if (hand == null || hand.Offset.Length != 3 || hand.Euler.Length != 3)
            return new Transform3D(Basis.FromEuler(new Vector3(Mathf.Pi / 2f, 0, 0)), new Vector3(0, Fist, 0));
        var e = new Vector3(Mathf.DegToRad(hand.Euler[0]), Mathf.DegToRad(hand.Euler[1]), Mathf.DegToRad(hand.Euler[2]));
        return new Transform3D(Basis.FromEuler(e, EulerOrder.Xyz), new Vector3(hand.Offset[0], hand.Offset[1], hand.Offset[2]));
    }

    /// <summary>
    /// The model-space transform that puts the model's grip socket on the hand frame: the socket's forward (−Z) turned
    /// onto the model's +Y and its origin moved to the frame's origin. Identity without a socket or a model.
    /// </summary>
    private Transform3D GripInverse(string? path)
    {
        if (path == null || _props.Socket(path, "grip") is not { } grip) return Transform3D.Identity;
        var forward = -grip.Basis.Z.Normalized();
        var turn = forward.IsFinite() && forward.LengthSquared() > 0.5f && forward.Dot(Vector3.Up) < 0.9999f
            ? new Basis(new Quaternion(forward, Vector3.Up))
            : Basis.Identity;
        return new Transform3D(turn, turn * -grip.Origin);
    }

    /// <summary>
    /// How far (cm) the held model's grip socket sits from the style's hand frame origin in the fist (probe
    /// <c>avatar.grip_error_cm</c>; −1 with nothing in hand or no grip socket). About 0 when the grip lands in the fist.
    /// </summary>
    public float GripErrorCm()
    {
        if (_held == null || _heldItem == null || !IsInstanceValid(_held) || !_held.IsInsideTree()) return -1f;
        if (!TrySocket("grip", out _) || _socketPath == null || _props.Socket(_socketPath, "grip") is not { } local
            || _held.GetParentNode3D() is not { } hand) return -1f;
        var target = hand.GlobalTransform * HandFrame(_game.Styles.Style(_game.Uses[_heldItem].Style).Hand).Origin;
        var grip = hand.GlobalTransform * _heldRest * local.Origin; // at rest: a thrust slides the model along its shaft
        return grip.DistanceTo(target) * 100f;
    }

    /// <summary>Wings ride the body as a lit attachment (they take its voxel light).</summary>
    private void LightWings(Node3D? old, Node3D? wings)
    {
        if (_visual == null) return;
        if (old != null) _visual.Detach(old);
        if (wings != null) _visual.AddLit(wings);
    }

    /// <summary>Whether the worn wings take the body's voxel light (probe <c>avatar.wings_lit</c>; 1 without wings).</summary>
    public bool WingsLit => _wings == null || (_visual != null && _visual.IsLit(_wings));
}
