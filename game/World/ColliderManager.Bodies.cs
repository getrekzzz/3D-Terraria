using System.Collections.Generic;
using System.Runtime.InteropServices;
using Godot;
using Strata.Core.Common;
using Strata.Core.Meshing;

namespace Strata.Game.World;

/// <summary>Physics-server bodies and shapes, one static body per non-empty collision bucket.</summary>
public sealed partial class ColliderManager
{
    /// <summary>(Re)builds the buckets whose geometry differs from the built bodies.</summary>
    private void Build(Int3 coord, ChunkCollision c)
    {
        using var profile = Strata.Core.Diagnostics.Metrics.Frame.Time("gd.colliders.build");
        int mask = 0;
        for (int i = 0; i < CollisionBuilder.HashCount; i++)
            if (c.Hashes[i] != c.BuiltHashes[i]) mask |= 1 << i;
        bool first = !c.Built;
        c.DirtyMask = 0;
        if (c.DataVersion >= c.PriorityVersion) c.Prioritized = false;
        c.Built = true;
        _built.Add(coord);
        if (mask == 0 && !first) return;

        var origin = ChunkMath.ChunkOrigin(coord);
        var transform = new Transform3D(Basis.Identity, new Vector3(origin.X, origin.Y, origin.Z) * Units.CellSize);
        var space = GetWorld3D().Space;
        for (int i = 0; i < CollisionBuilder.HashCount; i++)
        {
            if ((mask & (1 << i)) == 0) continue;
            bool platform = i >= CollisionBuilder.Buckets;
            var tris = platform ? c.Data.Platforms[i - CollisionBuilder.Buckets] : c.Data.Solid[i];
            // Create the replacement before freeing the old body, so there is never a frame without ground.
            Rid body = default, shape = default;
            if (tris.Count > 0) (body, shape) = CreateBody(tris, transform, space, platform ? PlatformLayer : TerrainLayer);
            FreeBucket(c, i);
            c.Bodies[i] = body;
            c.Shapes[i] = shape;
            c.BuiltHashes[i] = c.Hashes[i];
        }
        Rebuilt++;
        Versions[coord] = Versions.GetValueOrDefault(coord) + 1;
        BodyBuilt?.Invoke(coord);
    }

    private static (Rid Body, Rid Shape) CreateBody(List<System.Numerics.Vector3> tris, Transform3D transform, Rid space, uint layer)
    {
        var shape = PhysicsServer3D.ConcavePolygonShapeCreate();
        // Hand the triangles to the server straight from the list (no intermediate Vector3[] copy).
        var faces = MemoryMarshal.Cast<System.Numerics.Vector3, Vector3>(CollectionsMarshal.AsSpan(tris));
        using var packed = Variant.CreateFrom(faces);
        using var data = new Godot.Collections.Dictionary { ["faces"] = packed, ["backface_collision"] = true };
        PhysicsServer3D.ShapeSetData(shape, data);
        var body = PhysicsServer3D.BodyCreate();
        PhysicsServer3D.BodySetMode(body, PhysicsServer3D.BodyMode.Static);
        PhysicsServer3D.BodySetSpace(body, space);
        PhysicsServer3D.BodySetState(body, PhysicsServer3D.BodyState.Transform, transform);
        // One-way platforms live on their own layer; bodies moving upwards drop it from their mask.
        PhysicsServer3D.BodySetCollisionLayer(body, layer);
        PhysicsServer3D.BodySetCollisionMask(body, 0);
        PhysicsServer3D.BodyAddShape(body, shape);
        return (body, shape);
    }

    private static void FreeBucket(ChunkCollision c, int i)
    {
        if (c.Bodies[i].IsValid) PhysicsServer3D.FreeRid(c.Bodies[i]);
        if (c.Shapes[i].IsValid) PhysicsServer3D.FreeRid(c.Shapes[i]);
        c.Bodies[i] = default;
        c.Shapes[i] = default;
        c.BuiltHashes[i] = 0;
    }

    private void FreeBodies(Int3 coord, ChunkCollision c)
    {
        if (!c.Built) return;
        for (int i = 0; i < CollisionBuilder.HashCount; i++) FreeBucket(c, i);
        c.Built = false;
        c.DirtyMask = 0;
        c.Prioritized = false;
        _built.Remove(coord);
    }
}
