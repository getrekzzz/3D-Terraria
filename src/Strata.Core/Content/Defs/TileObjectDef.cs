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

    /// <summary>Levers and switches: flips and signals the wires touching it.</summary>
    Switch,
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

    /// <summary>A living flame drawn over the model (torches, candles).</summary>
    public FlameSpec? Flame { get; init; }

    /// <summary>Crafting station identities this object provides (e.g. "work_bench", "furnace").</summary>
    public string[] Stations { get; init; } = Array.Empty<string>();

    /// <summary>Housing roles (door, light, table, chair, bed).</summary>
    public string[] Housing { get; init; } = Array.Empty<string>();

    public int ContainerSlots { get; init; }

    /// <summary>
    /// Personal storage kind ("piggy_bank", "safe"): a chest-like object whose slots live on the character
    /// (<see cref="Gameplay.Player.PlayerState.Banks"/>), so every one of that kind opens the same contents.
    /// </summary>
    public string? SharedStorage { get; init; }

    public int HitPoints { get; init; } = 1;

    /// <summary>Item that places this object and is dropped when broken.</summary>
    public string? Item { get; init; }

    /// <summary>Extra loot table rolled when broken (pots, trees).</summary>
    public string? Loot { get; init; }

    /// <summary>Purely decorative plant/debris: cut by any swing, removed when support is lost.</summary>
    public bool Decor { get; init; }

    /// <summary>Only created by world generation (cannot be crafted/placed by players).</summary>
    public bool Natural { get; init; }

    /// <summary>Material sound of hits and breaks (dig_/break_&lt;sound&gt;); null: wood for axe objects, stone for pick ones.</summary>
    public string? Sound { get; init; }

    /// <summary>Colour of the chips a hit knocks off (#RRGGBB); null: a neutral wood or stone tone.</summary>
    public string? DustColor { get; init; }

    /// <summary>Leaf colour shed by hits on a tree (#RRGGBB), or null.</summary>
    public string? Leaves { get; init; }

    /// <summary>Falls over when felled (trees).</summary>
    public bool Topple { get; init; }

    /// <summary>The material sound with its default.</summary>
    public string HitSound => Sound ?? (BreakTool == BreakTool.Pick ? "stone" : "wood");

    /// <summary>Automatically derive the placing item (id = <see cref="Item"/> or this id) unless natural.</summary>
    public bool AutoItem { get; init; } = true;

    public int ItemValue { get; init; }
    public int ItemMaxStack { get; init; } = 99;
    public ItemCategory ItemCategory { get; init; } = ItemCategory.Furniture;

    /// <summary>Least tool power (of the break tool's kind) that can break it: demon altars need a Pwnhammer (80).</summary>
    public int MinPower { get; init; }

    /// <summary>What breaking it sets in motion (shadow orbs, crimson hearts, altars).</summary>
    public BreakEffect? OnBreak { get; init; }

    /// <summary>Cannot be broken until this world flag is set (Plantera's Bulbs wait for the mechanical bosses).</summary>
    public string? RequiresFlag { get; init; }

    /// <summary>A buff it gives players nearby (a campfire's Cozy Fire, a heart lantern's Heart Lamp).</summary>
    public AuraBuff? Aura { get; init; }

    /// <summary>An enemy banner: counts and empowers against these creatures (X-03).</summary>
    public BannerSpec? Banner { get; init; }

    /// <summary>A door that stays locked until this flag is set; using it with <see cref="Key"/> sets the flag.</summary>
    public string? LockedUntil { get; init; }

    /// <summary>Item consumed to unlock a door or to activate an altar (Temple Key, Lihzahrd Power Cell).</summary>
    public string? Key { get; init; }

    /// <summary>Boss an "activate" object summons when used with its <see cref="Key"/> (the Lihzahrd Altar calls Golem).</summary>
    public string? Summon { get; init; }

    /// <summary>Minecart track: a minecart rides along connected pieces (straight, up or down a step, or round a corner).</summary>
    public bool Track { get; init; }

    /// <summary>Signals its wires when a player steps onto it.</summary>
    public bool PressurePlate { get; init; }

    /// <summary>Fires this projectile when a wire signal reaches it (dart traps).</summary>
    public TrapDef? Trap { get; init; }

    /// <summary>A timer: while switched on (by hand or by a signal) it signals its wires every this many ticks.</summary>
    public int Timer { get; init; }

    /// <summary>A teleporter pad: a signal swaps whoever stands on it with whoever stands on the other pad it reaches.</summary>
    public bool Teleporter { get; init; }

    /// <summary>A statue: a signal makes it release a creature or drop an item (with caps).</summary>
    public StatueDef? Statue { get; init; }

    /// <summary>A logic gate ("and", "or", "nand", "nor", "xor", "xnor") reading the logic lamps stacked on it.</summary>
    public string? LogicGate { get; init; }

    /// <summary>A logic gate lamp: a signal toggles it; the gate beneath it re-evaluates.</summary>
    public bool LogicLamp { get; init; }

    /// <summary>Hurts players who touch it (dungeon spikes).</summary>
    public HazardSpec? Hazard { get; init; }

    /// <summary>The key a locked instance (<c>TileObjectInstance.Locked</c>) of this chest needs; unlocked ones open
    /// freely.</summary>
    public LockSpec? Lock { get; init; }

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

/// <summary>The creatures an enemy banner counts kills of and empowers nearby players against.</summary>
public sealed class BannerSpec
{
    public string[] Enemies { get; init; } = Array.Empty<string>();
}

/// <summary>A buff granted to players within <see cref="Radius"/> cells, refreshed while they stay close.</summary>
public sealed class AuraBuff
{
    public string Buff { get; init; } = "";
    public int Radius { get; init; } = 20;

    /// <summary>How long the buff lasts once the player walks away (ticks).</summary>
    public int Ticks { get; init; } = 120;
}

/// <summary>Contact damage an object deals to a player touching it (defense and immunity frames apply).</summary>
public sealed class HazardSpec
{
    public int Damage { get; init; } = 40;

    /// <summary>Knockback away from the object (Terraria knockback units).</summary>
    public float Knockback { get; init; } = 6f;
}

/// <summary>How a locked chest opens: with <see cref="Key"/> in the backpack (used up when <see cref="Consume"/>), and only
/// once <see cref="RequiresFlag"/> is set (with <see cref="Message"/> as the refusal before that).</summary>
public sealed class LockSpec
{
    public string Key { get; init; } = "";
    public bool Consume { get; init; } = true;
    public string? RequiresFlag { get; init; }
    public string? Message { get; init; }
}

/// <summary>What a wired trap shoots, along the way it faces.</summary>
public sealed class TrapDef
{
    public string Projectile { get; init; } = "";
    public int Damage { get; init; } = 20;

    /// <summary>Launch speed in m/s.</summary>
    public float Speed { get; init; } = 22f;
}

/// <summary>Where a flame burns on an object's model and how it looks.</summary>
public sealed class FlameSpec
{
    /// <summary>Base of the flame in model space (metres, Y up).</summary>
    public float[] At { get; init; } = { 0f, 0.4f, 0f };

    /// <summary>Flame height in metres.</summary>
    public float Size { get; init; } = 0.2f;

    /// <summary>Colour of the flame's body (its core burns near white).</summary>
    public string Color { get; init; } = "#ff8a2a";

    /// <summary>More flame bases of the same size and colour (the other candles of a candelabra or chandelier).</summary>
    public float[][] Also { get; init; } = Array.Empty<float[]>();
}

/// <summary>What a wired statue releases when signalled, and its caps.</summary>
public sealed class StatueDef
{
    /// <summary>Creature released (it drops no loot or coins).</summary>
    public string? Enemy { get; init; }

    /// <summary>Item dropped instead (heart, star, bomb statues).</summary>
    public string? Item { get; init; }

    public int Count { get; init; } = 1;

    /// <summary>Most of its creatures (or dropped items) allowed within <see cref="Radius"/> cells.</summary>
    public int Cap { get; init; } = 3;

    public int Radius { get; init; } = 100;

    /// <summary>Ticks before the same statue may release again.</summary>
    public int Cooldown { get; init; } = 30;
}
