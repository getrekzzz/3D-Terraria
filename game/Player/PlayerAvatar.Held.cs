using Godot;
using Strata.Core.Content;
using Strata.Core.Content.Defs;
using Strata.Game.Items;

namespace Strata.Game.Player;

/// <summary>
/// The held item's lighting and block cubes in third person (P3-06): the held node is attached to the hand as a lit
/// attachment of the body (<see cref="Rendering.CreatureVisual.AddLit"/>), so it darkens in caves and warms by torches
/// with the body; a held block is a small cube textured with the block's own terrain faces
/// (<see cref="BlockItemMeshes"/>) instead of a flat icon card. Sockets of the held model (muzzle, tip, string) are
/// exposed in world space for effects. The per-use-style hand frame (P3-02 style ids) is applied in HeldPose.
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
