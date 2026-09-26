namespace Strata.Core.Content.Defs;

public enum TileAnchor
{
    None, Floor, Ceiling, Wall, FloorOrWall, Any,
}

public enum TileCollision
{
    None, Solid, Platform, Trunk,
}

public enum TileInteraction
{
    None, Door, Chest, Sit, Sleep, Station, Read, Activate,
}

public enum BreakTool
{
    Any, Pick, Axe, Hammer,
}

/// <summary>
/// Multi-cell world object: furniture, crafting stations, doors, chests, torches, trees, plants, pots…
/// Placed on the voxel grid with an anchor cell and a rotation (0–3 quarter turns about +Y).
/// </summary>
public sealed class TileObjectDef : ContentDef
{
    /// <summary>Footprint in cells [x, y, z] before rotation.</summary>
    public int[] Size { get; init; } = { 1, 1, 1 };

    public TileAnchor Anchor { get; init; } = TileAnchor.Floor;
    public TileCollision Collision { get; init; } = TileCollision.None;
    public TileInteraction Interaction { get; init; } = TileInteraction.None;
    public BreakTool BreakTool { get; init; } = BreakTool.Any;

    public string? Model { get; init; }

    /// <summary>Number of procedural model variants (model path gets _vN suffix).</summary>
    public int Variants { get; init; } = 1;

    public bool Rotatable { get; init; } = true;

    public int[]? Light { get; init; }

    /// <summary>Crafting station identities this object provides (e.g. "work_bench", "furnace").</summary>
    public string[] Stations { get; init; } = Array.Empty<string>();

    /// <summary>Housing roles (door, light, table, chair, bed).</summary>
    public string[] Housing { get; init; } = Array.Empty<string>();

    public int ContainerSlots { get; init; }

    public int HitPoints { get; init; } = 1;

    /// <summary>Item that places this object and is dropped when broken.</summary>
    public string? Item { get; init; }

    /// <summary>Extra loot table rolled when broken (pots, trees).</summary>
    public string? Loot { get; init; }

    /// <summary>Purely decorative plant/debris: cut by any swing, removed when support is lost.</summary>
    public bool Decor { get; init; }

    /// <summary>Only created by world generation (cannot be crafted/placed by players).</summary>
    public bool Natural { get; init; }

    /// <summary>Automatically derive the placing item (id = <see cref="Item"/> or this id) unless natural.</summary>
    public bool AutoItem { get; init; } = true;

    public int ItemValue { get; init; }
    public int ItemMaxStack { get; init; } = 99;
    public ItemCategory ItemCategory { get; init; } = ItemCategory.Furniture;

    /// <summary>The item id that places/drops this object.</summary>
    public string? PlacingItemId => Item ?? (Natural ? null : Id);

    public int SizeX => Size.Length > 0 ? Size[0] : 1;
    public int SizeY => Size.Length > 1 ? Size[1] : 1;
    public int SizeZ => Size.Length > 2 ? Size[2] : 1;
}
