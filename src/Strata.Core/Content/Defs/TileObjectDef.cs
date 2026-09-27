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

    /// <summary>Least tool power (of the break tool's kind) that can break it: demon altars need a Pwnhammer (80).</summary>
    public int MinPower { get; init; }

    /// <summary>What breaking it sets in motion (shadow orbs, crimson hearts, altars).</summary>
    public BreakEffect? OnBreak { get; init; }

    /// <summary>The item id that places/drops this object.</summary>
    public string? PlacingItemId => Item ?? (Natural ? null : Id);

    public int SizeX => Size.Length > 0 ? Size[0] : 1;
    public int SizeY => Size.Length > 1 ? Size[1] : 1;
    public int SizeZ => Size.Length > 2 ? Size[2] : 1;
}

/// <summary>
/// World consequences of breaking an object: a world counter ticks up; its Nth break may raise a flag, and every
/// <see cref="Every"/>th summons a boss beside it; an announcement may follow; a meteorite may be set to fall.
/// </summary>
public sealed class BreakEffect
{
    /// <summary>World counter bumped by each break (e.g. "orbs", "altars").</summary>
    public string Counter { get; init; } = "";

    /// <summary>Flags raised on the 1st, 2nd, 3rd… break.</summary>
    public string[] Flags { get; init; } = Array.Empty<string>();

    /// <summary>Messages announced on the 1st, 2nd, 3rd… break (the last repeats; empty = none).</summary>
    public string[] Messages { get; init; } = Array.Empty<string>();

    /// <summary>Boss summoned on every <see cref="Every"/>th break.</summary>
    public string? Summon { get; init; }

    public int Every { get; init; } = 3;

    /// <summary>Chance that a meteorite falls the coming night.</summary>
    public float Meteor { get; init; }

    /// <summary>Creatures released at the break (altars loose wraiths in hardmode).</summary>
    public string? Release { get; init; }

    public float ReleaseChance { get; init; }
}
