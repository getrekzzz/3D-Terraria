namespace Strata.Core.Content.Defs;

public enum ItemCategory
{
    Material, Block, Tool, Weapon, Ammo, Armor, Accessory, Consumable, Potion, Placeable, Furniture,
    Station, Light, BossSummon, Key, Container, Vanity, Mount, Hook, Minecart, Pet, Dye, Fishing,
    Wiring, Coin, Misc,
}

public enum DamageClass
{
    None, Melee, Ranged, Magic, Summon, Generic,
}

public enum UseStyle
{
    None, Swing, Thrust, Shoot, Hold, Consume, Place,
}

public enum EquipSlot
{
    None, Head, Body, Legs, Accessory,
}

public sealed class BuffApplication
{
    public string Id { get; init; } = "";
    public int Ticks { get; init; }
}

/// <summary>
/// Data-driven item. Stat fields follow the reference game's item model; special mechanics are
/// attached through <see cref="Behaviors"/> (resolved by the game layer's behavior registry).
/// </summary>
public sealed class ItemDef : ContentDef
{
    public ItemCategory Category { get; init; } = ItemCategory.Material;
    public int Rarity { get; init; }
    public int MaxStack { get; init; } = 1;

    /// <summary>Base value in copper coins.</summary>
    public int Value { get; init; }

    // --- Use ---
    public UseStyle UseStyle { get; init; } = UseStyle.None;
    public int UseTime { get; init; } = 20;
    public int UseAnimation { get; init; } = 20;
    public bool AutoReuse { get; init; }
    public bool Consumable { get; init; }

    /// <summary>Interaction reach in cells (tools, placement).</summary>
    public int Reach { get; init; } = 10;

    // --- Combat ---
    public int Damage { get; init; }
    public DamageClass DamageType { get; init; } = DamageClass.None;
    public int Crit { get; init; }
    public float Knockback { get; init; }
    public string? Projectile { get; init; }

    /// <summary>Projectile launch speed in meters per second.</summary>
    public float ShootSpeed { get; init; }
    public int ManaCost { get; init; }

    /// <summary>Ammo class this weapon consumes (e.g. "arrow").</summary>
    public string? UseAmmo { get; init; }

    /// <summary>Ammo class this item belongs to (e.g. "arrow").</summary>
    public string? AmmoClass { get; init; }

    // --- Tools ---
    public int PickPower { get; init; }
    public int AxePower { get; init; }
    public int HammerPower { get; init; }

    /// <summary>Mining brush edge in cells (1 = precision). Defaults to 2 for pickaxes.</summary>
    public int MineBrush { get; init; } = 2;

    // --- Equipment ---
    public EquipSlot EquipSlot { get; init; } = EquipSlot.None;
    public int Defense { get; init; }
    public string? ArmorSet { get; init; }
    public TypedSpec[] EquipEffects { get; init; } = Array.Empty<TypedSpec>();

    /// <summary>Paint index this item applies (1–31; 0 = not a paint), used up by the Paintbrush and Paint Sprayer.</summary>
    public int Paint { get; init; }

    /// <summary>Colour a paint gives blocks ("#rrggbb").</summary>
    public string? PaintColor { get; init; }

    /// <summary>World progression flag raised the first time a player carries this item (milestones like the first bar smelted).</summary>
    public string? Milestone { get; init; }

    /// <summary>What Shimmer turns this item into (otherwise it comes apart into its recipe's ingredients).</summary>
    public string? Shimmer { get; init; }

    /// <summary>Set bonus text, shown while head, body and legs all belong to <see cref="ArmorSet"/>.</summary>
    public string? SetBonus { get; init; }

    /// <summary>Effects granted while the full set is worn (usually listed on the head piece).</summary>
    public TypedSpec[] SetEffects { get; init; } = Array.Empty<TypedSpec>();

    // --- Consumption ---
    public int HealLife { get; init; }
    public int HealMana { get; init; }
    public BuffApplication? Buff { get; init; }

    // --- Placement ---
    public string? PlaceBlock { get; init; }
    public string? PlaceObject { get; init; }

    /// <summary>Light emitted while held, [r, g, b] 0–15.</summary>
    public int[]? HeldLight { get; init; }

    // --- Presentation ---
    public string? Model { get; init; }
    public string? Icon { get; init; }
    public string? SoundSet { get; init; }
    public string? AnimationSet { get; init; }

    /// <summary>Items needed to fully research/duplicate (creative mode parity).</summary>
    public int ResearchCount { get; init; } = 1;

    public TypedSpec[] Behaviors { get; init; } = Array.Empty<TypedSpec>();

    public bool IsTool => PickPower > 0 || AxePower > 0 || HammerPower > 0;
    public bool IsWeapon => Damage > 0 && DamageType != DamageClass.None;
    public bool IsStackable => MaxStack > 1;
}
