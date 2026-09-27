using Godot;
using Strata.Core.Common;
using Strata.Core.Content.Defs;
using Strata.Core.Voxels;
using Strata.Core.Voxels.TileObjects;

namespace Strata.Game.World;

/// <summary>
/// Where an object's model is drawn: footprint centre with its quarter-turn yaw, doors swung open, lights mounted on
/// the wall they hang from (tilted away from it, base against the face), and floor objects settled onto smoothed
/// ground (natural terrain renders up to half a cell off the grid, so a model on the cell top can float or sink).
/// </summary>
public sealed class ObjectPlacement
{
    private const float WallTilt = 0.5f;
    private readonly WorldRuntime _runtime;

    public ObjectPlacement(WorldRuntime runtime) => _runtime = runtime;

    public Transform3D TransformOf(TileObjectInstance o)
    {
        var fp = o.Footprint;
        var origin = new Vector3(o.Anchor.X + fp.X * 0.5f, o.Anchor.Y, o.Anchor.Z + fp.Z * 0.5f) * Units.CellSize;
        var basis = new Basis(Vector3.Up, -o.Rotation * Mathf.Pi / 2f);
        if (o.Def.Interaction == TileInteraction.Door && o.Active)
        {
            // Swing about the hinge on the leaf's local -X edge.
            var hinge = new Vector3(-o.Def.SizeX * 0.5f * Units.CellSize, 0, 0);
            var swing = new Transform3D(Basis.Identity, hinge) * new Transform3D(new Basis(Vector3.Up, -Mathf.Pi / 2f), Vector3.Zero) * new Transform3D(Basis.Identity, -hinge);
            return new Transform3D(basis, origin) * swing;
        }
        if (WallSide(o) is { } wall)
        {
            // Lean away from the wall with the base on its face, a little above the cell floor.
            var d = new Vector3(wall.X, 0, wall.Z);
            var tilt = new Basis(d.Cross(Vector3.Up).Normalized(), WallTilt);
            var mount = origin + d * ((wall.X != 0 ? fp.X : fp.Z) * 0.5f * Units.CellSize - 0.02f) + Vector3.Up * 0.12f;
            return new Transform3D(tilt * basis, mount);
        }
        if (o.Def.Anchor is TileAnchor.Floor or TileAnchor.FloorOrWall or TileAnchor.Any) origin.Y = Ground(o, origin);
        return new Transform3D(basis, origin);
    }

    /// <summary>
    /// The horizontal direction of the wall a wall-capable object hangs from (nothing under it, a solid side), or null
    /// when it stands on the floor.
    /// </summary>
    private Int3? WallSide(TileObjectInstance o)
    {
        if (o.Def.Anchor is not (TileAnchor.Wall or TileAnchor.FloorOrWall or TileAnchor.Any)) return null;
        var world = _runtime.Session.World;
        var b = o.Bounds;
        if (o.Def.Anchor != TileAnchor.Wall)
            for (int z = b.Min.Z; z < b.Max.Z; z++)
                for (int x = b.Min.X; x < b.Max.X; x++)
                    if (world.IsSolid(x, b.Min.Y - 1, z)) return null;
        int y = b.Min.Y;
        if (world.IsSolid(b.Max.X, y, o.Anchor.Z)) return new Int3(1, 0, 0);
        if (world.IsSolid(b.Min.X - 1, y, o.Anchor.Z)) return new Int3(-1, 0, 0);
        if (world.IsSolid(o.Anchor.X, y, b.Max.Z)) return new Int3(0, 0, 1);
        if (world.IsSolid(o.Anchor.X, y, b.Min.Z - 1)) return new Int3(0, 0, -1);
        return null;
    }

    /// <summary>
    /// The rendered ground height under an object resting on natural (smoothed) terrain, found by a ray against the
    /// terrain colliders near the player; the cell floor elsewhere or on built (crisp) blocks.
    /// </summary>
    private float Ground(TileObjectInstance o, Vector3 origin)
    {
        var world = _runtime.Session.World;
        var below = new Int3(Mathf.FloorToInt(origin.X / Units.CellSize), o.Anchor.Y - 1, Mathf.FloorToInt(origin.Z / Units.CellSize));
        ushort block = world.GetBlock(below);
        if (block == 0 || block == VoxelWorld.Unknown || _runtime.Session.Content.Blocks[block].Render != BlockRender.Smooth) return origin.Y;
        var space = _runtime.GetWorld3D()?.DirectSpaceState;
        if (space == null) return origin.Y;
        const float reach = 0.3f;
        var query = PhysicsRayQueryParameters3D.Create(origin + Vector3.Up * reach, origin + Vector3.Down * reach, ColliderManager.TerrainLayer);
        var hit = space.IntersectRay(query);
        return hit.Count > 0 ? ((Vector3)hit["position"]).Y : origin.Y;
    }
}
