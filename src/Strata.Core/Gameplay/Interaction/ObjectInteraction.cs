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

    /// <summary>A locked door, or an altar used without its key item (a message explains).</summary>
    Refused,

    /// <summary>An altar consumed its key and called its boss.</summary>
    Activated,

    /// <summary>A lever or switch was flipped (its wires carried the signal).</summary>
    Switched,
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
            case TileInteraction.Door when obj.Def.LockedUntil is { } flag && !_world.Progression.IsSet(flag):
                if (obj.Def.Key == null || !TakeKey(p, obj.Def.Key))
                {
                    Refused?.Invoke(p, "It's locked.");
                    return InteractOutcome.Refused;
                }
                _world.Progression.Set(flag);
                obj.Active = true;
                MarkModified(obj);
                StateChanged?.Invoke(obj);
                return InteractOutcome.DoorToggled;
            case TileInteraction.Activate when obj.Def.Summon != null:
                return Activate(p, obj);
            case TileInteraction.Switch:
                obj.Active = !obj.Active;
                NotifyChanged(obj);
                _world.Wires.Signal(obj);
                return InteractOutcome.Switched;
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

    /// <summary>A player was refused (locked door, missing key item) with a message.</summary>
    public event Action<PlayerEntity, string>? Refused;

    private InteractOutcome Activate(PlayerEntity p, TileObjectInstance obj)
    {
        var content = _world.Session.Content;
        if (!content.Enemies.TryGet(obj.Def.Summon!, out var boss) || _world.Npcs.All.Any(n => n.Alive && n.Def == boss)) return InteractOutcome.None;
        if (obj.Def.Key != null && !TakeKey(p, obj.Def.Key))
        {
            Refused?.Invoke(p, content.Items.TryGet(obj.Def.Key, out var key) ? $"It needs a {key.DisplayName}." : "Nothing happens.");
            return InteractOutcome.Refused;
        }
        var at = (new System.Numerics.Vector3(obj.Anchor.X, obj.Anchor.Y + obj.Footprint.Y, obj.Anchor.Z) + new System.Numerics.Vector3(obj.Footprint.X, 4, obj.Footprint.Z) * 0.5f) * Units.CellSize;
        _world.Npcs.Spawn(boss, at);
        return InteractOutcome.Activated;
    }

    private static bool TakeKey(PlayerEntity p, string itemId)
    {
        var backpack = p.State.Inventory.Backpack;
        for (int i = 0; i < backpack.Size; i++)
            if (backpack[i].Item?.Id == itemId)
            {
                backpack.Take(i, 1);
                return true;
            }
        return false;
    }

    /// <summary>Whether a door currently blocks movement (closed doors are solid).</summary>
    public static bool Blocks(TileObjectInstance obj) => obj.BlocksMovement;

    /// <summary>Opens or closes a door for a non-player walker (town folk); refuses to close on a body in the way.</summary>
    public bool SetDoor(TileObjectInstance door, bool open, IEnumerable<(System.Numerics.Vector3 Min, System.Numerics.Vector3 Max)> bodiesInCells)
    {
        if (!door.IsDoor || door.Active == open) return false;
        if (door.Def.LockedUntil is { } flag && !_world.Progression.IsSet(flag)) return false; // residents cannot pick locks
        if (!open)
        {
            var b = door.Bounds;
            foreach (var (min, max) in bodiesInCells)
                if (min.X < b.Max.X && max.X > b.Min.X && min.Y < b.Max.Y && max.Y > b.Min.Y && min.Z < b.Max.Z && max.Z > b.Min.Z) return false;
        }
        door.Active = open;
        MarkModified(door);
        StateChanged?.Invoke(door);
        return true;
    }

    private static bool Overlaps(PlayerEntity p, TileObjectInstance obj)
    {
        var (min, max) = p.CellBox;
        var b = obj.Bounds;
        return min.X < b.Max.X && max.X > b.Min.X && min.Y < b.Max.Y && max.Y > b.Min.Y && min.Z < b.Max.Z && max.Z > b.Min.Z;
    }

    /// <summary>Records a state change made elsewhere (wiring) so it saves and redraws.</summary>
    public void NotifyChanged(TileObjectInstance obj)
    {
        MarkModified(obj);
        StateChanged?.Invoke(obj);
    }

    private void MarkModified(TileObjectInstance obj)
    {
        var chunk = _world.Session.World.GetChunk(ChunkMath.CellToChunk(obj.Anchor));
        if (chunk != null) chunk.Modified = true;
    }
}
