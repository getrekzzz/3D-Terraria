using Strata.Core.Common;
using Strata.Core.Content.Defs;
using Strata.Core.Session;
using Strata.Core.Voxels.TileObjects;

namespace Strata.Core.Gameplay.Interaction;

public enum InteractOutcome
{
    None,
    DoorToggled,
    DoorBlocked,
    ContainerOpened,
    SpawnSet,
    Seated,
}

/// <summary>"Interact" on tile objects: doors, containers, beds and seats.</summary>
public sealed class ObjectInteraction
{
    private readonly GameWorld _world;

    public ObjectInteraction(GameWorld world) => _world = world;

    /// <summary>A container was opened by a player (the UI shows it).</summary>
    public event Action<PlayerEntity, TileObjectInstance>? ContainerOpened;

    /// <summary>An object's state (door open/closed) changed; colliders and visuals update.</summary>
    public event Action<TileObjectInstance>? StateChanged;

    public InteractOutcome Interact(PlayerEntity p, TileObjectInstance obj)
    {
        switch (obj.Def.Interaction)
        {
            case TileInteraction.Door:
                // A closing door must not trap the player inside its cells.
                if (obj.Active && Overlaps(p, obj)) return InteractOutcome.DoorBlocked;
                obj.Active = !obj.Active;
                MarkModified(obj);
                StateChanged?.Invoke(obj);
                return InteractOutcome.DoorToggled;
            case TileInteraction.Chest:
                _world.Editor.EnsureContainerLoot(obj);
                ContainerOpened?.Invoke(p, obj);
                return InteractOutcome.ContainerOpened;
            case TileInteraction.Sleep:
                p.State.SpawnPoints[_world.Session.Settings.Name] = new[] { obj.Anchor.X, obj.Anchor.Y + obj.Footprint.Y, obj.Anchor.Z };
                return InteractOutcome.SpawnSet;
            case TileInteraction.Sit:
                return InteractOutcome.Seated;
            default:
                return InteractOutcome.None;
        }
    }

    /// <summary>Whether a door currently blocks movement (closed doors are solid).</summary>
    public static bool Blocks(TileObjectInstance obj) =>
        obj.Def.Collision == TileCollision.Solid && !(obj.Def.Interaction == TileInteraction.Door && obj.Active);

    private static bool Overlaps(PlayerEntity p, TileObjectInstance obj)
    {
        var (min, max) = p.CellBox;
        var b = obj.Bounds;
        return min.X < b.Max.X && max.X > b.Min.X && min.Y < b.Max.Y && max.Y > b.Min.Y && min.Z < b.Max.Z && max.Z > b.Min.Z;
    }

    private void MarkModified(TileObjectInstance obj)
    {
        var chunk = _world.Session.World.GetChunk(ChunkMath.CellToChunk(obj.Anchor));
        if (chunk != null) chunk.Modified = true;
    }
}
