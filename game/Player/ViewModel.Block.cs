using Godot;
using Strata.Core.Content.Defs;
using Strata.Game.Items;

namespace Strata.Game.Player;

/// <summary>
/// First-person block cubes (P3-06): a held block is a small cube with the block's terrain faces
/// (<see cref="BlockItemMeshes"/>), lit by the voxel light like any held model, instead of an unshaded icon card.
/// </summary>
public sealed partial class ViewModel
{
    /// <summary>The cube's scale in view (a 0.2 m cube this close to the eye would fill the corner).</summary>
    private const float BlockScale = 0.6f;

    /// <summary>Whether the first-person item is a textured block cube (probe <c>view.held_block_cube</c>).</summary>
    public bool HeldBlockCube => _geometry is MeshInstance3D { Mesh: ArrayMesh mesh } && mesh.SurfaceGetMaterial(0) == BlockItemMeshes.Material;

    /// <summary>The first-person item when it has no model: a block's cube, else its icon as a card.</summary>
    private Node3D FallbackModel(ItemDef item)
    {
        if (BlockItemMeshes.ForItem(item, _game.Session.Content, _controller.Runtime.Services.Render) is { } cube)
        {
            var mi = new MeshInstance3D { Mesh = cube, Scale = Vector3.One * BlockScale, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            _geometry = mi;
            return mi;
        }
        return new Sprite3D { Texture = ItemIcons.Get(item), PixelSize = 0.18f / 128f, Shaded = false };
    }
}
