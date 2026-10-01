using System.Collections.Generic;
using Godot;
using Strata.Core.Common;
using Strata.Core.Content.Defs;
using Strata.Core.Gameplay.Interaction;
using Strata.Core.Session;
using Strata.Core.Voxels.TileObjects;

namespace Strata.Game.World;

/// <summary>
/// Box colliders for solid tile objects near the focus: closed doors and chests (full footprint), tree trunks
/// (a central column) and furniture platforms (a thin top slab on the platform layer).
/// </summary>
public sealed partial class ObjectColliders : Node3D
{
    private readonly Dictionary<long, (Rid Body, BoxShape3D Shape)> _bodies = new();
    private GameWorld _game = null!;
    private Int3 _lastFocusChunk = new(int.MinValue, 0, 0);
    private Int3 _focusCell;

    /// <summary>Radius in cells within which objects get bodies.</summary>
    public int Radius { get; set; } = 48;

    private WorldRuntime _runtime = null!;

    public void Initialize(WorldRuntime runtime)
    {
        _runtime = runtime;
        var game = runtime.Game;
        _game = game;
        var store = game.Session.World.TileObjects;
        store.Added += obj =>
        {
            if (Near(obj)) Refresh(obj);
        };
        store.Removed += obj => Free(obj.Id);
        game.Interaction.StateChanged += Refresh;
    }

    private bool Near(TileObjectInstance obj) =>
        obj.Bounds.Intersects(new Aabb3i(_focusCell - Int3.One * Radius, _focusCell + Int3.One * Radius));

    public override void _PhysicsProcess(double delta) => UpdateFocus(_runtime.FocusCell);

    public void UpdateFocus(Int3 focusCell)
    {
        _focusCell = focusCell;
        var chunk = ChunkMath.CellToChunk(focusCell);
        if (chunk == _lastFocusChunk) return;
        _lastFocusChunk = chunk;
        var box = new Aabb3i(focusCell - Int3.One * Radius, focusCell + Int3.One * Radius);
        var keep = new HashSet<long>();
        foreach (var obj in _game.Session.World.TileObjects.InBox(box))
        {
            keep.Add(obj.Id);
            if (!_bodies.ContainsKey(obj.Id)) Refresh(obj);
        }
        var stale = new List<long>();
        foreach (var id in _bodies.Keys)
            if (!keep.Contains(id)) stale.Add(id);
        foreach (var id in stale) Free(id);
    }

    private void Refresh(TileObjectInstance obj)
    {
        Free(obj.Id);
        var shape = ShapeFor(obj, out uint layer);
        if (shape == null) return;
        var (min, max) = shape.Value;
        var size = (max - min) * Units.CellSize;
        var center = (min + max) * 0.5f * Units.CellSize;
        var box = new BoxShape3D { Size = size };
        var body = PhysicsServer3D.BodyCreate();
        PhysicsServer3D.BodySetMode(body, PhysicsServer3D.BodyMode.Static);
        PhysicsServer3D.BodySetSpace(body, GetWorld3D().Space);
        PhysicsServer3D.BodySetState(body, PhysicsServer3D.BodyState.Transform, new Transform3D(Basis.Identity, center));
        PhysicsServer3D.BodySetCollisionLayer(body, layer);
        PhysicsServer3D.BodySetCollisionMask(body, 0);
        PhysicsServer3D.BodyAddShape(body, box.GetRid());
        _bodies[obj.Id] = (body, box);
    }

    /// <summary>Collision box in cell units, or null when the object does not collide.</summary>
    private static (Vector3 Min, Vector3 Max)? ShapeFor(TileObjectInstance obj, out uint layer)
    {
        layer = ColliderManager.ObjectLayer;
        var b = obj.Bounds;
        var min = new Vector3(b.Min.X, b.Min.Y, b.Min.Z);
        var max = new Vector3(b.Max.X, b.Max.Y, b.Max.Z);
        switch (obj.Def.Collision)
        {
            case TileCollision.Solid:
                return ObjectInteraction.Blocks(obj) ? (min, max) : null;
            case TileCollision.Trunk:
            {
                // Shared with targeting: only an axe can select the canopy, everything else stops at this column.
                var (tMin, tMax) = obj.TrunkBox;
                return (new Vector3(tMin.X, tMin.Y, tMin.Z), new Vector3(tMax.X, tMax.Y, tMax.Z));
            }
            case TileCollision.Platform:
                layer = ColliderManager.PlatformLayer;
                return (new Vector3(min.X, max.Y - 0.2f, min.Z), max);
            default:
                return null;
        }
    }

    private void Free(long id)
    {
        if (!_bodies.Remove(id, out var entry)) return;
        PhysicsServer3D.FreeRid(entry.Body);
    }

    public override void _ExitTree()
    {
        foreach (var (body, _) in _bodies.Values) PhysicsServer3D.FreeRid(body);
        _bodies.Clear();
    }
}
