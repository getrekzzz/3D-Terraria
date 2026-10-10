namespace Strata.Core.Content.Specs;

/// <summary>Parameters for item use behaviours (executed by the game layer's behaviour registry).</summary>
public static class ItemBehaviorSpecs
{
    public const string MeleeSwing = "meleeSwing";
    public const string Thrust = "thrust";
    public const string ShootProjectile = "shootProjectile";

    /// <summary>A whip: lashes along a path, hitting every creature on it, and tags them for minions (P3-14).</summary>
    public const string Whip = "whip";
    public const string MineTool = "mineTool";
    public const string ChopTool = "chopTool";
    public const string HammerTool = "hammerTool";
    public const string PlaceBlock = "placeBlock";
    public const string PlaceObject = "placeObject";
    public const string Consume = "consume";
    public const string SummonBoss = "summonBoss";
    public const string StartEvent = "startEvent";
    public const string Bucket = "bucket";
    public const string IncinerationSummon = "incinerationSummon";

    /// <summary>A grappling hook (fired with the grapple key from anywhere in the inventory).</summary>
    public const string Grapple = "grapple";

    /// <summary>A fishing rod: casts its bobber projectile, and reels it (and any catch) back in.</summary>
    public const string Fishing = "fishing";

    /// <summary>Fishing bait: adds its bait power to the rod's; one may be used up with each catch.</summary>
    public const string Bait = "bait";

    /// <summary>Bait that hooks a boss instead of a fish (a Truffle Worm in the ocean hooks Duke Fishron).</summary>
    public const string BaitSummon = "baitSummon";

    /// <summary>Lays wire of a colour (Red, Blue, Green, Yellow Wrench), places actuators, or cuts both (Wire Cutter).</summary>
    public const string Wiring = "wiring";

    /// <summary>Swings a net that catches critters (Bug Net).</summary>
    public const string BugNet = "bugNet";

    /// <summary>Lets a caught critter go.</summary>
    public const string ReleaseCritter = "releaseCritter";

    /// <summary>Summons a rideable mount, or dismounts (Slimy Saddle, Fuzzy Carrot, Honeyed Goggles, Cosmic Car Key).</summary>
    public const string Mount = "mount";

    /// <summary>Boards a minecart on the track beneath the player.</summary>
    public const string Minecart = "minecart";

    /// <summary>Paints the aimed block with the first paint in the backpack, or scrapes paint off (Paintbrush, Paint Scraper).</summary>
    public const string PaintTool = "paint";

    /// <summary>Grass seeds: the aimed top block (one of <c>on</c>) turns into <c>grass</c>; one seed is used.</summary>
    public const string PlantGrass = "plantGrass";

    /// <summary>A town pet license (Cat, Dog, Bunny): sets world flag <c>flag</c> once; a used license is refused.</summary>
    public const string License = "license";

    /// <summary>Dyes the user's hair (Life, Mana, Depth, Money, Time… Hair Dye; the remover clears it).</summary>
    public const string HairDye = "hairDye";

    public static void Register(SpecCatalog c)
    {
        c.Register<MeleeSwingParams>(SpecKind.ItemBehavior, MeleeSwing);
        c.Register<ThrustParams>(SpecKind.ItemBehavior, Thrust);
        c.Register<ShootProjectileParams>(SpecKind.ItemBehavior, ShootProjectile);
        c.Register<WhipParams>(SpecKind.ItemBehavior, Whip);
        c.Register<ToolParams>(SpecKind.ItemBehavior, MineTool);
        c.Register<ToolParams>(SpecKind.ItemBehavior, ChopTool);
        c.Register<ToolParams>(SpecKind.ItemBehavior, HammerTool);
        c.Register<PlaceParams>(SpecKind.ItemBehavior, PlaceBlock);
        c.Register<PlaceParams>(SpecKind.ItemBehavior, PlaceObject);
        c.Register<ConsumeParams>(SpecKind.ItemBehavior, Consume);
        c.Register<SummonBossParams>(SpecKind.ItemBehavior, SummonBoss);
        c.Register<StartEventParams>(SpecKind.ItemBehavior, StartEvent);
        c.Register<BucketParams>(SpecKind.ItemBehavior, Bucket);
        c.Register<IncinerationSummonParams>(SpecKind.ItemBehavior, IncinerationSummon);
        c.Register<GrappleParams>(SpecKind.ItemBehavior, Grapple);
        c.Register<FishingParams>(SpecKind.ItemBehavior, Fishing);
        c.Register<BaitParams>(SpecKind.ItemBehavior, Bait);
        c.Register<BaitSummonParams>(SpecKind.ItemBehavior, BaitSummon);
        c.Register<WiringParams>(SpecKind.ItemBehavior, Wiring);
        c.Register<BugNetParams>(SpecKind.ItemBehavior, BugNet);
        c.Register<ReleaseCritterParams>(SpecKind.ItemBehavior, ReleaseCritter);
        c.Register<MinecartParams>(SpecKind.ItemBehavior, Minecart);
        c.Register<Gameplay.Mounts.MountParams>(SpecKind.ItemBehavior, Mount);
        c.Register<PaintToolParams>(SpecKind.ItemBehavior, PaintTool);
        c.Register<PlantGrassParams>(SpecKind.ItemBehavior, PlantGrass);
        c.Register<LicenseParams>(SpecKind.ItemBehavior, License);
        c.Register<HairDyeParams>(SpecKind.ItemBehavior, HairDye);
    }
}

public sealed class MeleeSwingParams : SpecParams, IValidatableSpec
{
    /// <summary>Horizontal swing arc in degrees.</summary>
    public float Arc { get; init; } = 150f;

    /// <summary>Blade reach in cells (hitbox length from the hand).</summary>
    public float Length { get; init; } = 3f;

    public string? TrailColor { get; init; }

    public string? Validate() => Arc is <= 0 or > 360 ? "arc must be in (0, 360]" : Length <= 0 ? "length must be > 0" : null;
}

/// <summary>A hook: flies out along the aim up to its reach, latches onto a block and reels the player in.</summary>
public sealed class GrappleParams : SpecParams, IValidatableSpec
{
    /// <summary>Reach in cells, hook flight speed and reel-in speed (m/s).</summary>
    public float Reach { get; init; } = 18f;

    public float Speed { get; init; } = 28f;

    public float Pull { get; init; } = 12f;

    /// <summary>Chain colour (hex).</summary>
    public string Color { get; init; } = "#9a9aa4";

    /// <summary>The item whose model is drawn as the hook's head (null = the hook item itself; P3-17').</summary>
    public string? Head { get; init; }

    /// <summary>How the line is drawn: <c>links</c> (a metal chain), <c>vine</c> (Ivy Whip) or <c>gem</c> (glowing gem links).</summary>
    public string Chain { get; init; } = "links";

    public static readonly string[] Chains = { "links", "vine", "gem" };

    public string? Validate() =>
        Reach <= 0 || Speed <= 0 || Pull <= 0 ? "reach, speed and pull must be > 0"
        : Array.IndexOf(Chains, Chain) < 0 ? $"chain must be one of {string.Join(", ", Chains)}" : null;
}

public sealed class ThrustParams : SpecParams
{
    public float Length { get; init; } = 3f;
}

public sealed class ShootProjectileParams : SpecParams, IValidatableSpec
{
    public int Count { get; init; } = 1;
    public float Spread { get; init; }

    /// <summary>
    /// How many of this weapon's projectiles one player may have out at once; a use that would exceed it is refused.
    /// Unset means one for returning projectiles (boomerangs, flails) and no limit otherwise; 0 means no limit.
    /// </summary>
    public int? MaxActive { get; init; }

    /// <summary>The live-projectile limit for a projectile that returns after <paramref name="returns"/> ticks (0 = none).</summary>
    public int Limit(int returns) => MaxActive ?? (returns > 0 ? 1 : 0);

    /// <summary>
    /// Where the shot starts: "muzzle" (the weapon's business end, the default), "sky" (<see cref="SkyHeight"/> above the
    /// crosshair point, scattered by <see cref="SkyScatter"/>, falling onto it: Starfury, Daedalus Stormbow, Star Wrath),
    /// "cursor" (at the crosshair point) or "eye". Minions always appear at the crosshair point.
    /// </summary>
    public string From { get; init; } = "muzzle";

    /// <summary>Metres above the crosshair point a sky shot starts.</summary>
    public float SkyHeight { get; init; } = 16f;

    /// <summary>Metres a sky shot's start may stray sideways (it still falls onto the crosshair point).</summary>
    public float SkyScatter { get; init; } = 3f;

    public string? Validate() =>
        Count < 1 ? "count must be >= 1"
        : MaxActive < 0 ? "maxActive must be >= 0"
        : MaxActive > 0 && Count > MaxActive ? "count must not exceed maxActive (the weapon could never fire)"
        : From is not ("muzzle" or "sky" or "cursor" or "eye") ? "from must be muzzle, sky, cursor or eye"
        : SkyHeight is <= 0f or > 64f || SkyScatter is < 0f or > 16f ? "skyHeight must be within (0, 64] and skyScatter within [0, 16]" : null;
}

public sealed class ToolParams : SpecParams
{
}

public sealed class PlaceParams : SpecParams
{
}

public sealed class ConsumeParams : SpecParams
{
    /// <summary>Permanently raises max life by this amount (life-crystal style), up to <see cref="LifeCap"/>.</summary>
    public int PermanentLife { get; init; }

    public int LifeCap { get; init; } = 400;

    /// <summary>Permanently raises max mana (mana-crystal style), up to <see cref="ManaCap"/>.</summary>
    public int PermanentMana { get; init; }

    public int ManaCap { get; init; } = 200;

    /// <summary>
    /// Usable only once the player's max life (crystals and fruit, not buffs) is at least this much (Life Fruit: 400);
    /// otherwise the use is refused with "Requires N maximum life".
    /// </summary>
    public int RequiresLife { get; init; }

    /// <summary>Usable only once this progression flag is set (Life Fruit: a mechanical boss defeated).</summary>
    public string? RequiresFlag { get; init; }

    /// <summary>What a use refused for <see cref="RequiresFlag"/> says (silent when null).</summary>
    public string? FlagMessage { get; init; }

    /// <summary>Returns the user to their spawn point (Magic Mirror, Recall Potion).</summary>
    public bool Recall { get; init; }

    /// <summary>Opens into this loot table (crates, treasure bags).</summary>
    public string? Loot { get; init; }

    /// <summary>Permanently unlocks the Expert-only sixth accessory slot (Demon Heart); usable once, only in Expert and Master worlds.</summary>
    public bool ExtraAccessorySlot { get; init; }

    /// <summary>Permanently upgrades the player's minecart (Minecart Upgrade Kit): faster; usable once.</summary>
    public bool MinecartUpgrade { get; init; }
}

public sealed class BaitSummonParams : SpecParams
{
    public string Enemy { get; init; } = "";

    /// <summary>Biomes the bobber must float in.</summary>
    public string[] Biomes { get; init; } = Array.Empty<string>();
}

public sealed class FishingParams : SpecParams
{
    /// <summary>Fishing power: faster bites and better catches (crates, rare fish).</summary>
    public int Power { get; init; } = 5;

    /// <summary>The bobber survives lava (Hotline Fishing Hook), so lava can be fished without other gear.</summary>
    public bool Lava { get; init; }
}

/// <summary>Bait power (Terraria percentages: Worm 25, Master Bait 50); higher power is also used up less often.</summary>
public sealed class BaitParams : SpecParams, IValidatableSpec
{
    public int Power { get; init; } = 10;
    public string? Validate() => Power is < 1 or > 100 ? "power must be in [1,100]" : null;
}

/// <summary>Starts a world event (a moon, an invasion) when its conditions hold; fails while it is already running.</summary>
public sealed class StartEventParams : SpecParams
{
    public string Event { get; init; } = "";

    /// <summary>Conditions evaluated at the player's position.</summary>
    public TypedSpec[] Conditions { get; init; } = Array.Empty<TypedSpec>();

    public string? FailMessage { get; init; }
}

/// <summary>Summons a boss near the player when its conditions hold (e.g. night, a biome).</summary>
public sealed class SummonBossParams : SpecParams
{
    public string Enemy { get; init; } = "";

    /// <summary>Conditions evaluated at the player's position.</summary>
    public TypedSpec[] Conditions { get; init; } = Array.Empty<TypedSpec>();

    /// <summary>Spawn distance from the player (m).</summary>
    public float Distance { get; init; } = 25f;

    /// <summary>Message shown when the conditions fail.</summary>
    public string? FailMessage { get; init; }

    /// <summary>
    /// A town resident cursed into the boss instead (the Clothier Voodoo Doll): it appears <see cref="Height"/> m
    /// above them while they live, and they vanish into it.
    /// </summary>
    public string? Curse { get; init; }

    public float Height { get; init; } = 7f;

    /// <summary>Message shown when the cursed resident is not alive in the world.</summary>
    public string? CurseFailMessage { get; init; }
}

/// <summary>
/// Scoops or pours one cell of liquid. Without <see cref="Liquid"/> the item is an empty bucket that collects
/// whatever liquid it is aimed at and becomes that liquid's bucket item; with it, pouring returns <see cref="Empty"/>.
/// </summary>
public sealed class BucketParams : SpecParams
{
    /// <summary>Liquid held (null = empty bucket).</summary>
    public string? Liquid { get; init; }

    /// <summary>Item left after pouring (the empty bucket).</summary>
    public string? Empty { get; init; }

    /// <summary>Minimum levels (of 31) the aimed pool must provide to fill the bucket.</summary>
    public int MinCollect { get; init; } = 16;

    /// <summary>Soaks up this liquid without ever filling (the Super Absorbant Sponge).</summary>
    public string? Absorbs { get; init; }

    /// <summary>Never runs dry: pouring keeps the item (the bottomless buckets), so it needs no <see cref="Empty"/>.</summary>
    public bool Bottomless { get; init; }
}

/// <summary>
/// Summons a boss when the item is destroyed by a liquid (thrown into lava) in the given world layer — the wayfinder's
/// effigy. When <see cref="KillsResident"/> is set that resident must be alive, and dies.
/// </summary>
public sealed class IncinerationSummonParams : SpecParams, IValidatableSpec
{
    public string Enemy { get; init; } = "";

    /// <summary>World layer the item must burn in (surface, underground, cavern, underworld…).</summary>
    public string Layer { get; init; } = "underworld";

    public string? KillsResident { get; init; }

    public string? Validate() =>
        string.IsNullOrWhiteSpace(Enemy) ? "enemy is required"
        : !Enum.TryParse<World.WorldLayer>(Layer, ignoreCase: true, out _) ? $"layer '{Layer}' is not a world layer" : null;
}

/// <summary>Lays wire (using up <see cref="Wire"/> items) or, with <see cref="Cut"/>, removes it and gives the wire back.</summary>
public sealed class WiringParams : SpecParams, IValidatableSpec
{
    /// <summary>Wire Cutter: removes every wire colour and the actuator from the aimed cell.</summary>
    public bool Cut { get; init; }

    /// <summary>Item used up per placement (Wire for wrenches; the Actuator places itself).</summary>
    public string Wire { get; init; } = "wire";

    /// <summary>Wire colour laid: "red", "blue", "green" or "yellow".</summary>
    public string Color { get; init; } = "red";

    /// <summary>Places an actuator on the aimed block instead of wire.</summary>
    public bool Actuator { get; init; }

    public string? Validate() => Color is "red" or "blue" or "green" or "yellow" ? null : $"unknown wire colour '{Color}'";
}

/// <summary>Catches the nearest creature with <see cref="Tag"/> in front of the player; it becomes its catch item.</summary>
public sealed class BugNetParams : SpecParams
{
    public string Tag { get; init; } = "critter";

    /// <summary>Reach in cells.</summary>
    public float Range { get; init; } = 3.5f;
}

/// <summary>Releases a caught creature where the player aims.</summary>
public sealed class ReleaseCritterParams : SpecParams
{
    public string Enemy { get; init; } = "";
}

/// <summary>A rideable minecart: top speed and acceleration along the track, in m/s and m/s².</summary>
public sealed class MinecartParams : SpecParams
{
    public float MaxSpeed { get; init; } = 18f;
    public float Acceleration { get; init; } = 14f;
}

/// <summary>A paint tool: the Paintbrush paints, the Paint Scraper (<see cref="Scrape"/>) takes paint off.</summary>
public sealed class PaintToolParams : SpecParams
{
    public bool Scrape { get; init; }
}

public sealed class PlantGrassParams : SpecParams, IValidatableSpec
{
    /// <summary>The grass block the seeds grow into.</summary>
    public string Grass { get; init; } = "";

    /// <summary>Blocks the seeds take on (dirt for Grass Seeds, mud for Jungle and Mushroom Grass Seeds).</summary>
    public string[] On { get; init; } = Array.Empty<string>();

    public string? Validate() => Grass.Length == 0 || On.Length == 0 ? "plantGrass needs grass and on" : null;
}

public sealed class LicenseParams : SpecParams, IValidatableSpec
{
    /// <summary>The world flag the license sets (a pet's arrival waits for it).</summary>
    public string Flag { get; init; } = "";

    public string? Validate() => Flag.Length == 0 ? "license needs a flag" : null;
}
