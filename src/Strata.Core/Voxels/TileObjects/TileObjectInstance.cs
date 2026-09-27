using Strata.Core.Common;
using Strata.Core.Content.Defs;
using Strata.Core.Gameplay.Items;

namespace Strata.Core.Voxels.TileObjects;

/// <summary>
/// A placed multi-cell object. Position/definition are immutable (safe to read from jobs);
/// gameplay state (hit points, door state, container) is main-thread only.
/// </summary>
public sealed class TileObjectInstance
{
    public TileObjectInstance(TileObjectDef def, Int3 anchor, int rotation, int variant)
    {
        Def = def;
        Anchor = anchor;
        Rotation = rotation & 3;
        Variant = Math.Clamp(variant, 0, Math.Max(0, def.Variants - 1));
        HitPoints = def.HitPoints;
        if (def.ContainerSlots > 0) Container = new ItemStack[def.ContainerSlots];
    }

    /// <summary>Session-unique id (not persisted).</summary>
    public long Id { get; internal set; }

    public TileObjectDef Def { get; }

    /// <summary>Minimum corner of the occupied cells (after rotation).</summary>
    public Int3 Anchor { get; }

    public int Rotation { get; }
    public int Variant { get; }

    public Int3 Footprint => FootprintOf(Def, Rotation);

    /// <summary>Solid objects stop bodies; an open door does not.</summary>
    public bool BlocksMovement => Def.Collision == TileCollision.Solid && !(Def.Interaction == TileInteraction.Door && Active);

    public bool IsDoor => Def.Interaction == TileInteraction.Door;
    public Aabb3i Bounds => new(Anchor, Anchor + Footprint);

    public int HitPoints { get; set; }

    /// <summary>Door open, lever on, etc.</summary>
    public bool Active { get; set; }

    public ItemStack[]? Container { get; }

    /// <summary>Generated loot has been rolled into the container (lazily on first open).</summary>
    public bool LootRolled { get; set; }

    /// <summary>Loot table to roll into the container the first time it is opened (world-generated chests).</summary>
    public string? PendingLoot { get; set; }

    public static Int3 FootprintOf(TileObjectDef def, int rotation) =>
        (rotation & 1) == 0 ? new Int3(def.SizeX, def.SizeY, def.SizeZ) : new Int3(def.SizeZ, def.SizeY, def.SizeX);

    public override string ToString() => $"{Def.Id}@{Anchor} r{Rotation}";
}
