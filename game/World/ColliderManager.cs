using System.Collections.Generic;
using System.Linq;
using Godot;
using Strata.Core.Common;
using Strata.Core.Meshing;

namespace Strata.Game.World;

/// <summary>
/// Static trimesh colliders for chunks near the physics focus only (the player). Collision data is kept for
/// every meshed chunk so bodies can be created instantly when the player approaches.
/// </summary>
public sealed partial class ColliderManager : Node3D
{
    public const uint TerrainLayer = 1;
    public const uint PlatformLayer = 2;

    /// <summary>Solid tile objects (closed doors, chests, tree trunks).</summary>
    public const uint ObjectLayer = 4;

    private sealed class ChunkCollision
    {
        public CollisionBuilder Data = null!;
        public Rid Body;
        public Rid PlatformBody;
        public readonly List<Shape3D> Shapes = new();
        public bool Dirty = true;
    }

    private readonly Dictionary<Int3, ChunkCollision> _chunks = new();

    /// <summary>Chunks (Chebyshev distance) around the focus that get physics bodies.</summary>
    public int Radius { get; set; } = 2;

    public int BodyCount { get; private set; }

    /// <summary>A chunk's terrain body was (re)built (objects resting on it can settle onto the smoothed ground).</summary>
    public event System.Action<Int3>? BodyBuilt;

    /// <summary>Version counter per chunk, bumped whenever its collider is rebuilt (tests assert on it).</summary>
    public Dictionary<Int3, int> Versions { get; } = new();

    public void SetData(Int3 coord, CollisionBuilder data)
    {
        if (!_chunks.TryGetValue(coord, out var c)) _chunks[coord] = c = new ChunkCollision();
        c.Data = data;
        c.Dirty = true;
    }

    public void Remove(Int3 coord)
    {
        if (_chunks.Remove(coord, out var c)) FreeBody(c);
    }

    public bool HasBody(Int3 coord) => _chunks.TryGetValue(coord, out var c) && c.Body.IsValid;

    /// <summary>Creates, rebuilds or frees bodies according to distance from the focus chunk.</summary>
    /// <summary>
    /// Keeps bodies for chunks near the focus. Chunks touching the focus chunk are built at once; the rest share a
    /// small per-frame time budget so walking into new ground never stalls a frame.
    /// </summary>
    public void UpdateFocus(Int3 focusChunk, double budgetMs = 2.0)
    {
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        foreach (var (coord, c) in _chunks)
        {
            var d = coord - focusChunk;
            bool near = d.ChebyshevLength <= Radius;
            if (!near)
            {
                if (c.Body.IsValid) FreeBody(c);
                continue;
            }
            if (!c.Dirty && c.Body.IsValid) continue;
            bool urgent = d.ChebyshevLength <= 1;
            if (!urgent && System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds >= budgetMs) continue;
            Build(coord, c);
        }
    }

    private void Build(Int3 coord, ChunkCollision c)
    {
        FreeBody(c);
        c.Dirty = false;
        var origin = ChunkMath.ChunkOrigin(coord);
        var transform = new Transform3D(Basis.Identity, new Vector3(origin.X, origin.Y, origin.Z) * Units.CellSize);
        var body = PhysicsServer3D.BodyCreate();
        PhysicsServer3D.BodySetMode(body, PhysicsServer3D.BodyMode.Static);
        PhysicsServer3D.BodySetSpace(body, GetWorld3D().Space);
        PhysicsServer3D.BodySetState(body, PhysicsServer3D.BodyState.Transform, transform);
        PhysicsServer3D.BodySetCollisionLayer(body, TerrainLayer);
        PhysicsServer3D.BodySetCollisionMask(body, 0);
        AddShapes(body, c, c.Data.Solid);
        c.Body = body;
        if (c.Data.Platforms.Any(l => l.Count > 0))
        {
            // One-way platforms live on their own layer; bodies moving upwards drop it from their mask.
            var platforms = PhysicsServer3D.BodyCreate();
            PhysicsServer3D.BodySetMode(platforms, PhysicsServer3D.BodyMode.Static);
            PhysicsServer3D.BodySetSpace(platforms, GetWorld3D().Space);
            PhysicsServer3D.BodySetState(platforms, PhysicsServer3D.BodyState.Transform, transform);
            PhysicsServer3D.BodySetCollisionLayer(platforms, PlatformLayer);
            PhysicsServer3D.BodySetCollisionMask(platforms, 0);
            AddShapes(platforms, c, c.Data.Platforms);
            c.PlatformBody = platforms;
        }
        BodyCount++;
        Versions[coord] = Versions.GetValueOrDefault(coord) + 1;
        BodyBuilt?.Invoke(coord);
    }

    private static void AddShapes(Rid body, ChunkCollision c, List<System.Numerics.Vector3>[] buckets)
    {
        foreach (var tris in buckets)
        {
            if (tris.Count == 0) continue;
            var shape = new ConcavePolygonShape3D { BackfaceCollision = true };
            shape.SetFaces(MeshUpload.Faces(tris));
            PhysicsServer3D.BodyAddShape(body, shape.GetRid());
            c.Shapes.Add(shape);
        }
    }

    private void FreeBody(ChunkCollision c)
    {
        if (!c.Body.IsValid) return;
        PhysicsServer3D.FreeRid(c.Body);
        c.Body = default;
        if (c.PlatformBody.IsValid) PhysicsServer3D.FreeRid(c.PlatformBody);
        c.PlatformBody = default;
        c.Shapes.Clear();
        BodyCount--;
    }

    public override void _ExitTree()
    {
        foreach (var c in _chunks.Values) FreeBody(c);
        _chunks.Clear();
    }
}
