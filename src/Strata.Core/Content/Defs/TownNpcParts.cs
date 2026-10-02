namespace Strata.Core.Content.Defs;

/// <summary>How a town NPC lives in the world: housed resident or keeper of a landmark (later: visitor, pet).</summary>
public enum TownNpcKind
{
    Resident,
    Landmark,
}

/// <summary>Something a resident sells. Price defaults to the item's value.</summary>
public sealed class ShopEntry
{
    public string Item { get; init; } = "";

    /// <summary>Price in copper before happiness scaling (null = item value).</summary>
    public long? Price { get; init; }

    /// <summary>Shop tab the entry is listed under ("shop", or e.g. "decor" for the Painter).</summary>
    public string Tab { get; init; } = ShopTabs.Shop;

    /// <summary>
    /// Item paid instead of coins (the Tavernkeep's Defender Medals); the price is then a count of it and is not
    /// scaled by happiness. Null = coins.
    /// </summary>
    public string? Currency { get; init; }

    /// <summary>Stock appears only while these hold (time, flags, biome of the home…).</summary>
    public TypedSpec[] Conditions { get; init; } = Array.Empty<TypedSpec>();
}

/// <summary>A chat line; the first matching conditions are eligible and one is picked.</summary>
public sealed class DialogueLine
{
    /// <summary>Text with placeholders: {player}, {name} (the resident), {resident:&lt;id&gt;} (another resident's name).</summary>
    public string Text { get; init; } = "";

    public TypedSpec[] Conditions { get; init; } = Array.Empty<TypedSpec>();
}

/// <summary>A happiness preference: a biome around the home or a neighbour. Positive scores are liked.</summary>
public sealed class MoodEntry
{
    public string? Biome { get; init; }
    public string? Neighbor { get; init; }

    /// <summary>A layer instead of a biome: "underground" (Terraria's Underground, any depth below the surface).</summary>
    public string? Layer { get; init; }

    /// <summary>+2 loves, +1 likes, −1 dislikes, −2 hates.</summary>
    public int Score { get; init; }
}

public static class ShopTabs
{
    public const string Shop = "shop";
}

/// <summary>
/// A town NPC that must be found and freed before moving in (Terraria's bound Goblin, Mechanic, Wizard…): appears
/// bound somewhere out of the player's sight once <see cref="Requires"/> holds, at a cell where <see cref="Where"/>
/// holds (or at a planned <see cref="Landmark"/>), and talking to them frees them (flag "rescued_&lt;id&gt;").
/// </summary>
public sealed class RescueInfo
{
    public static readonly string[] Poses = { "bound", "sleeping", "webbed", "unconscious", "tortured" };

    /// <summary>World conditions for the bound NPC to appear at all.</summary>
    public TypedSpec[] Requires { get; init; } = Array.Empty<TypedSpec>();

    /// <summary>Conditions judged at a candidate cell (biome, layer…).</summary>
    public TypedSpec[] Where { get; init; } = Array.Empty<TypedSpec>();

    /// <summary>A planned landmark to appear at instead of searching (the Dungeon's Mechanic room).</summary>
    public string? Landmark { get; init; }

    public string Pose { get; init; } = "bound";

    /// <summary>Name while bound ("Bound Goblin").</summary>
    public string Title { get; init; } = "";

    /// <summary>Freeing needs (and uses up) one of this item (the Tortured Soul's Purification Powder).</summary>
    public string? RequiresItem { get; init; }

    /// <summary>Said when the player cannot free them yet ({item}).</summary>
    public string NeedsItemText { get; init; } = "Maybe some {item} would help.";

    /// <summary>Said when freed ({player}).</summary>
    public string Thanks { get; init; } = "Thank you, {player}!";

    /// <summary>Horizontal search shell around the player (m).</summary>
    public float MinDistance { get; init; } = 30f;
    public float MaxDistance { get; init; } = 70f;
}

/// <summary>
/// How a resident defends itself when hostile creatures come near (Terraria's town NPC attacks): throw, shoot or
/// magic launch <see cref="Projectile"/> (a friendly projectile that never hurts players or terrain), melee strikes
/// within reach, heal mends the most hurt resident or player in range, ward gives <see cref="Buff"/> to players and
/// residents nearby while a threat is about. Damage doubles in hardmode (<see cref="HardmodeMultiplier"/>).
/// </summary>
public sealed class TownAttack
{
    public static readonly string[] Styles = { "throw", "shoot", "magic", "melee", "heal", "ward" };

    public string Style { get; init; } = "throw";
    public string? Projectile { get; init; }
    public int Damage { get; init; }
    public float HardmodeMultiplier { get; init; } = 2f;
    public float Knockback { get; init; } = 3f;

    /// <summary>Launch speed (m/s).</summary>
    public float Speed { get; init; } = 14f;

    /// <summary>Threat detection range (m); also the reach of heal and ward.</summary>
    public float Range { get; init; } = 14f;

    /// <summary>Ticks between attacks.</summary>
    public int Interval { get; init; } = 60;

    public int Count { get; init; } = 1;

    /// <summary>Random spread per shot (degrees).</summary>
    public float Spread { get; init; }

    /// <summary>The ward's buff and how long each pulse lasts.</summary>
    public string? Buff { get; init; }
    public int BuffTicks { get; init; } = 120;

    /// <summary>Extra defense a ward gives residents (players get <see cref="Buff"/>).</summary>
    public int WardDefense { get; init; } = 8;

    /// <summary>Life a heal restores.</summary>
    public int Heal { get; init; }
}
