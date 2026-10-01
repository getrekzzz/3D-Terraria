using System.Numerics;
using Strata.Core.Common;
using Strata.Core.Content;
using Strata.Core.Voxels;
using Strata.Core.Voxels.TileObjects;

namespace Strata.Core.Gameplay.Interaction;

/// <summary>What the crosshair points at.</summary>
public sealed record Target(Int3 Cell, Int3 Normal, ushort Block, TileObjectInstance? Object, Vector3 HitCells, float Distance)
{
    /// <summary>Hit position within the cell, each axis 0–1 (drives brush alignment).</summary>
    public Vector3 Fraction => HitCells - new Vector3(Cell.X, Cell.Y, Cell.Z);

    /// <summary>The empty cell in front of the hit face (placement target).</summary>
    public Int3 Adjacent => Cell + Normal;

    public bool IsObject => Object != null;
}

/// <summary>Which soft objects a targeting ray may stop at (blocks and other objects always stop it).</summary>
[Flags]
public enum TargetFilter
{
    /// <summary>The ray passes through decor and tree canopies; only trunks, furniture and blocks stop it.</summary>
    None = 0,

    /// <summary>Decor objects (grass, flowers, bushes, stalagmites, rubble) stop the ray.</summary>
    Decor = 1,

    /// <summary>The whole box of a tree-like object (<see cref="Content.Defs.TileCollision.Trunk"/>) stops the ray, canopy included.</summary>
    TreeCanopy = 2,

    All = Decor | TreeCanopy,
}

/// <summary>Voxel-exact targeting: DDA through cells, stopping at solid (or non-air) blocks and tile objects.</summary>
public static class Targeting
{
    /// <summary>
    /// Casts from <paramref name="originMeters"/> along <paramref name="direction"/> up to <paramref name="reachMeters"/>.
    /// Liquids are ignored; decor objects are hit only when <paramref name="includeDecor"/> is set. Tree canopies
    /// always stop the ray (the behaviour of the console and test helpers).
    /// </summary>
    public static Target? Cast(VoxelWorld world, Vector3 originMeters, Vector3 direction, float reachMeters, bool includeDecor = true) =>
        Cast(world, originMeters, direction, reachMeters, includeDecor ? TargetFilter.All : TargetFilter.TreeCanopy);

    /// <summary>
    /// Casts from <paramref name="originMeters"/> along <paramref name="direction"/> up to <paramref name="reachMeters"/>.
    /// Liquids are ignored; <paramref name="filter"/> says whether decor and tree canopies stop the ray (a trunk
    /// column always does).
    /// </summary>
    public static Target? Cast(VoxelWorld world, Vector3 originMeters, Vector3 direction, float reachMeters, TargetFilter filter)
    {
        if (direction.LengthSquared() < 1e-10f) return null;
        direction = Vector3.Normalize(direction);
        var origin = originMeters / Units.CellSize;
        var test = new FilteredHit(world, filter, origin, direction);
        var hit = Walk(world, origin, direction, reachMeters / Units.CellSize, ref test);
        if (hit == null) return null;
        var (cell, normal, t, blockId) = hit.Value;
        return new Target(cell, normal, blockId, test.Object, origin + direction * t, t * Units.CellSize);
    }

    /// <summary>
    /// The crosshair ray of a camera that sits behind (and beside) the eye: the walk starts where the ray crosses the
    /// eye's plane (<paramref name="startT"/> metres from the camera), so nothing between the camera and the player can
    /// be selected. It covers <paramref name="reachMeters"/> plus the ray's sideways offset from the eye, and the
    /// returned <see cref="Target.Distance"/> is measured from <paramref name="eye"/>.
    /// </summary>
    public static Target? CastView(VoxelWorld world, Vector3 camera, Vector3 dir, Vector3 eye, float reachMeters, TargetFilter filter, out float startT)
    {
        startT = 0;
        if (dir.LengthSquared() < 1e-10f) return null;
        dir = Vector3.Normalize(dir);
        startT = MathF.Max(0f, Vector3.Dot(eye - camera, dir));
        var start = camera + dir * startT;
        float lateral = Vector3.Distance(eye, start);
        var origin = start / Units.CellSize;
        var test = new FilteredHit(world, filter, origin, dir);
        var hit = Walk(world, origin, dir, (reachMeters + lateral + 0.5f) / Units.CellSize, ref test);
        if (hit == null) return null;
        var (cell, normal, t, blockId) = hit.Value;
        var at = origin + dir * t;
        return new Target(cell, normal, blockId, test.Object, at, Vector3.Distance(at * Units.CellSize, eye));
    }

    /// <summary>
    /// The raw cell walk with a caller-supplied stop test (grapple latches, shot origins): the first cell along the
    /// ray where <paramref name="isHit"/> is true, as a <see cref="Target"/> without an object (distance in metres from
    /// <paramref name="originMeters"/>), or null when nothing stops it within reach or the ray leaves loaded terrain.
    /// </summary>
    public static Target? CastWhere(VoxelWorld world, Vector3 originMeters, Vector3 direction, float reachMeters, Func<Int3, ushort, bool> isHit)
    {
        if (direction.LengthSquared() < 1e-10f) return null;
        direction = Vector3.Normalize(direction);
        var origin = originMeters / Units.CellSize;
        var hit = Cast(world, origin, direction, reachMeters / Units.CellSize, isHit);
        if (hit == null) return null;
        var (cell, normal, t, blockId) = hit.Value;
        return new Target(cell, normal, blockId, null, origin + direction * t, t * Units.CellSize);
    }

    /// <summary>
    /// The ground under the middle of the feet (its top face), for digging straight down: the first solid cell at most
    /// <paramref name="depth"/> cells below the feet, or null over a drop.
    /// </summary>
    public static Target? UnderFeet(VoxelWorld world, Vector3 feetMeters, int depth = 3)
    {
        var at = feetMeters / Units.CellSize;
        int x = (int)MathF.Floor(at.X), z = (int)MathF.Floor(at.Z);
        int top = (int)MathF.Floor(at.Y + 0.2f); // bodies rest a little into the smoothed surface
        for (int y = top; y >= top - depth; y--)
        {
            var cell = new Int3(x, y, z);
            ushort block = world.GetBlock(cell);
            if (block == VoxelWorld.Unknown) return null;
            if (block == 0 || world.Content.Blocks[block].Render == Content.Defs.BlockRender.None) continue;
            var hit = new Vector3(at.X, y + 1, at.Z);
            return new Target(cell, Int3.Up, block, null, hit, Vector3.Distance(hit, at) * Units.CellSize);
        }
        return null;
    }

    /// <summary>
    /// Walks the ray for a bucket: the first liquid cell (<c>Liquid</c> true) or, when the ray meets a
    /// solid block or runs out of reach first, the last open cell before it (where poured liquid would go).
    /// </summary>
    public static (Int3 Cell, bool Liquid)? CastLiquid(VoxelWorld world, Vector3 originMeters, Vector3 direction, float reachMeters)
    {
        if (direction.LengthSquared() < 1e-10f) return null;
        direction = Vector3.Normalize(direction);
        var origin = originMeters / Units.CellSize;
        float reach = reachMeters / Units.CellSize;
        var content = world.Content;
        Int3? lastOpen = null;
        var hit = Cast(world, origin, direction, reach, (cell, block) =>
        {
            if (world.GetLiquid(cell.X, cell.Y, cell.Z) != 0) return true;
            if (block != 0 && content.Blocks[block].IsSolid) return true;
            lastOpen = cell;
            return false;
        });
        if (hit is { } h && world.GetLiquid(h.Cell.X, h.Cell.Y, h.Cell.Z) != 0) return (h.Cell, true);
        if (hit == null)
        {
            // Out of reach without hitting anything: pour at the end of the reach.
            var end = origin + direction * reach;
            var cell = new Int3((int)MathF.Floor(end.X), (int)MathF.Floor(end.Y), (int)MathF.Floor(end.Z));
            return world.GetBlock(cell) == VoxelWorld.Unknown ? null : (cell, false);
        }
        return lastOpen is { } open ? (open, false) : null;
    }

    /// <summary>A stop test for <see cref="Walk{T}"/> (a struct, so the per-tick crosshair casts allocate no closure).</summary>
    private interface ICellTest
    {
        bool IsHit(Int3 cell, ushort block);
    }

    private struct FuncHit : ICellTest
    {
        public Func<Int3, ushort, bool> Test;

        public bool IsHit(Int3 cell, ushort block) => Test(cell, block);
    }

    /// <summary>
    /// Rendered blocks and tile objects stop the ray; decor only with <see cref="TargetFilter.Decor"/>, and a tree-like
    /// object only where the ray crosses its trunk column unless <see cref="TargetFilter.TreeCanopy"/> is set.
    /// </summary>
    private struct FilteredHit : ICellTest
    {
        private readonly VoxelWorld _world;
        private readonly TargetFilter _filter;
        private readonly Vector3 _origin, _dir;

        /// <summary>The object that stopped the ray (null for a block).</summary>
        public TileObjectInstance? Object;

        /// <param name="world">The world walked.</param>
        /// <param name="filter">Which soft objects stop the ray.</param>
        /// <param name="origin">Ray origin in cell units.</param>
        /// <param name="dir">Normalized ray direction.</param>
        public FilteredHit(VoxelWorld world, TargetFilter filter, Vector3 origin, Vector3 dir)
        {
            _world = world;
            _filter = filter;
            _origin = origin;
            _dir = dir;
            Object = null;
        }

        public bool IsHit(Int3 cell, ushort block)
        {
            if (block != 0 && _world.Content.Blocks[block].Render != Content.Defs.BlockRender.None) return true;
            var o = _world.TileObjects.At(cell);
            if (o == null) return false;
            if (o.Def.Decor && (_filter & TargetFilter.Decor) == 0) return false;
            if (o.Def.Collision == Content.Defs.TileCollision.Trunk && (_filter & TargetFilter.TreeCanopy) == 0 && !CrossesTrunk(o, cell)) return false;
            Object = o;
            return true;
        }

        /// <summary>
        /// The ray passes through the part of <paramref name="o"/>'s trunk column inside <paramref name="cell"/>. The
        /// column is the body collider's exact box, so one narrower or wider than whole cells (mahogany, giant
        /// mushrooms) is hit exactly where it stops the player, not wherever a cell centre happens to fall.
        /// </summary>
        private readonly bool CrossesTrunk(TileObjectInstance o, Int3 cell)
        {
            var (trunkMin, trunkMax) = o.TrunkBox;
            var lo = new Vector3(cell.X, cell.Y, cell.Z);
            var min = Vector3.Max(trunkMin, lo);
            var max = Vector3.Min(trunkMax, lo + Vector3.One);
            if (min.X >= max.X || min.Y >= max.Y || min.Z >= max.Z) return false; // the column does not reach into this cell
            float enter = 0f, exit = float.PositiveInfinity;
            return Slab(_origin.X, _dir.X, min.X, max.X, ref enter, ref exit)
                && Slab(_origin.Y, _dir.Y, min.Y, max.Y, ref enter, ref exit)
                && Slab(_origin.Z, _dir.Z, min.Z, max.Z, ref enter, ref exit);
        }

        /// <summary>Narrows the ray's [enter, exit] interval to one axis of a box; false once it is empty.</summary>
        private static bool Slab(float o, float d, float min, float max, ref float enter, ref float exit)
        {
            if (MathF.Abs(d) < 1e-8f) return o >= min && o <= max;
            float a = (min - o) / d, b = (max - o) / d;
            if (a > b) (a, b) = (b, a);
            enter = MathF.Max(enter, a);
            exit = MathF.Min(exit, b);
            return enter <= exit;
        }
    }

    private static (Int3 Cell, Int3 Normal, float T, ushort Block)? Cast(VoxelWorld world, Vector3 origin, Vector3 dir, float maxT, Func<Int3, ushort, bool> isHit)
    {
        var test = new FuncHit { Test = isHit };
        return Walk(world, origin, dir, maxT, ref test);
    }

    /// <summary>Walks cells along the ray (cell units) until <paramref name="test"/> stops it, reach runs out or terrain is unloaded.</summary>
    private static (Int3 Cell, Int3 Normal, float T, ushort Block)? Walk<T>(VoxelWorld world, Vector3 origin, Vector3 dir, float maxT, ref T test)
        where T : struct, ICellTest
    {
        int x = (int)MathF.Floor(origin.X), y = (int)MathF.Floor(origin.Y), z = (int)MathF.Floor(origin.Z);
        int sx = Math.Sign(dir.X), sy = Math.Sign(dir.Y), sz = Math.Sign(dir.Z);
        float dx = sx != 0 ? MathF.Abs(1f / dir.X) : float.PositiveInfinity;
        float dy = sy != 0 ? MathF.Abs(1f / dir.Y) : float.PositiveInfinity;
        float dz = sz != 0 ? MathF.Abs(1f / dir.Z) : float.PositiveInfinity;
        float tx = sx != 0 ? (sx > 0 ? x + 1 - origin.X : origin.X - x) * dx : float.PositiveInfinity;
        float ty = sy != 0 ? (sy > 0 ? y + 1 - origin.Y : origin.Y - y) * dy : float.PositiveInfinity;
        float tz = sz != 0 ? (sz > 0 ? z + 1 - origin.Z : origin.Z - z) * dz : float.PositiveInfinity;
        var normal = Int3.Zero;
        float t = 0;
        while (t <= maxT)
        {
            var cell = new Int3(x, y, z);
            ushort block = world.GetBlock(cell);
            if (block == VoxelWorld.Unknown) return null;
            if (test.IsHit(cell, block)) return (cell, normal, t, block);
            if (tx < ty && tx < tz) { x += sx; t = tx; tx += dx; normal = new Int3(-sx, 0, 0); }
            else if (ty < tz) { y += sy; t = ty; ty += dy; normal = new Int3(0, -sy, 0); }
            else { z += sz; t = tz; tz += dz; normal = new Int3(0, 0, -sz); }
        }
        return null;
    }
}
