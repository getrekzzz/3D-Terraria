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

/// <summary>
/// How an item is used on screen, Terraria's use styles (JSON camelCase, e.g. "holdUp"). The style also implies some
/// actions (<see cref="Gameplay.Items.UseStyleRules"/>): a melee swing, a thrust, a shot, drinking a potion.
/// <see cref="None"/> in data is resolved from the item's stats (tools swing, placers place, potions are drunk).
/// Each style's body clip, arm aim and grip live in data/config/use_styles.json.
/// </summary>
public enum UseStyle
{
    None,
    /// <summary>Overhead swing: swords, tools, summon staves, fishing rods, nets, buckets, wiring and paint tools.</summary>
    Swing,
    /// <summary>A quick jab: shortswords.</summary>
    Rapier,
    /// <summary>A two-handed thrust along the aim: spears and tridents.</summary>
    Spear,
    /// <summary>Held out along the aim (grappling hooks and anything without a better style).</summary>
    Shoot,
    /// <summary>Held like a gun, including magic guns.</summary>
    Gun,
    Bow,
    /// <summary>Staffs and wands point at the target.</summary>
    Staff,
    /// <summary>Spell tomes are read from the off hand.</summary>
    Tome,
    /// <summary>Boomerangs, shuriken, throwing knives, bombs and grenades.</summary>
    Throw,
    Yoyo,
    Flail,
    Whip,
    /// <summary>Raised overhead: boss and event summons, life and mana crystals, mirrors, crates.</summary>
    HoldUp,
    Drink,
    Eat,
    Place,
}

/// <summary>
/// Where an item is worn. Pet through Hook are the misc equipment row, in the order of
/// <see cref="Gameplay.Items.MiscSlot"/> (JSON: "pet", "lightPet", "minecart", "mount", "hook").
/// </summary>
public enum EquipSlot
{
    None, Head, Body, Legs, Accessory, Pet, LightPet, Minecart, Mount, Hook,
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
    /// <summary>
    /// What every stackable item stacks to (Terraria 1.4.4: potions, ammo, blocks, materials, summons, furniture);
    /// only the copper, silver and gold coins stop at 100. Checked by the content validator.
    /// </summary>
    public const int CommonMaxStack = 9999;

    public ItemCategory Category { get; init; } = ItemCategory.Material;
    public int Rarity { get; init; }
    public int MaxStack { get; init; } = 1;

    /// <summary>Base value in copper coins.</summary>
    public int Value { get; init; }

    // --- Use ---
    public UseStyle UseStyle { get; init; } = UseStyle.None;

    /// <summary>Chance (0–1) a shot keeps its ammo (Megashark 0.5), on top of ammo-saving gear.</summary>
    public float AmmoSaveChance { get; init; }
    public int UseTime { get; init; } = 20;
    public int UseAnimation { get; init; } = 20;
    public bool AutoReuse { get; init; }

    /// <summary>
    /// Ticks after a use animation ends before the next may start (Terraria's reuseDelay): the pause between the
    /// Clockwork Assault Rifle's bursts. Speed prefixes do not shorten it.
    /// </summary>
    public int ReuseDelay { get; init; }

    /// <summary>
    /// A burst (several shots inside one animation, use time shorter than use animation) uses ammo for its first shot
    /// only, as the Clockwork Assault Rifle does in Terraria; other bursts use ammo per shot.
    /// </summary>
    public bool AmmoPerBurst { get; init; }
    public bool Consumable { get; init; }

    /// <summary>
    /// The swing or thrust is animation only and the blade never hits (Terraria's noMelee: Zenith fights with its
    /// projectiles alone). Stops <see cref="UseStyle"/> from implying a melee attack.
    /// </summary>
    public bool NoMelee { get; init; }

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

    /// <summary>Left on the ground, it vanishes when day breaks (Fallen Stars, P3-30).</summary>
    public bool FadesAtDawn { get; init; }

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
