using Strata.Core.Determinism;
using Strata.Core.World;

namespace Strata.Core.Conditions;

/// <summary>
/// Everything a data-driven condition may inspect. Reused between evaluations to avoid allocation;
/// callers fill only the fields relevant to their domain (spawning, crafting, shops, events…).
/// </summary>
public sealed class ConditionContext
{
    private static readonly HashSet<string> Empty = new();

    /// <summary>Biome ids applying at the position (any order).</summary>
    public IReadOnlyCollection<string> Biomes { get; set; } = Array.Empty<string>();

    public WorldLayer Layer { get; set; } = WorldLayer.Surface;

    /// <summary>Clock time in hours [0, 24).</summary>
    public float Hour { get; set; } = 12f;

    public bool IsDay { get; set; } = true;

    /// <summary>0 = full moon … 7.</summary>
    public int MoonPhase { get; set; }

    /// <summary>Cells below the local surface (negative when above ground).</summary>
    public float Depth { get; set; }

    /// <summary>Effective light level 0–15 at the position.</summary>
    public int Light { get; set; } = 15;

    public IReadOnlySet<string> Flags { get; set; } = Empty;
    public IReadOnlySet<string> Events { get; set; } = Empty;

    /// <summary>Kills counted so far by each active event (its waves).</summary>
    public IReadOnlyDictionary<string, int> EventKills { get; set; } = new Dictionary<string, int>();
    public IReadOnlySet<string> NearbyLiquids { get; set; } = Empty;

    /// <summary>Copper value of the coins the (richest) player carries.</summary>
    public long PlayerCoins { get; set; }

    /// <summary>Highest maximum life among the players.</summary>
    public int PlayerMaxLife { get; set; }

    /// <summary>Town residents currently living in the world (town NPC ids).</summary>
    public IReadOnlySet<string> Residents { get; set; } = Empty;

    /// <summary>How many of an item (or items with a tag, "#tag") the players carry; null = unknown (0).</summary>
    public Func<string, int>? ItemCount { get; set; }

    /// <summary>Whether a player has an item equipped (armor or accessory); null = unknown (no).</summary>
    public Func<string, bool>? Wearing { get; set; }

    /// <summary>Defense of the player the check is made for (boss enrage).</summary>
    public int PlayerDefense { get; set; }

    /// <summary>World difficulty (0 Classic, 1 Expert, 2 Master, 3 Journey).</summary>
    public int Difficulty { get; set; }

    /// <summary>The world's evil ("corruption" or "crimson").</summary>
    public string Evil { get; set; } = "";

    /// <summary>The weather at the position (rain turns to snow over the tundra; clear underground).</summary>
    public Gameplay.Weather.LocalWeather Weather { get; set; }

    /// <summary>A windy day is blowing.</summary>
    public bool Windy { get; set; }

    /// <summary>The seasonal event running ("halloween", "christmas"; "" for none).</summary>
    public string Season { get; set; } = "";

    /// <summary>The world's secret seed twists (X-07).</summary>
    public World.SecretSeed SecretSeeds { get; set; }

    /// <summary>Fishing power of the rod making a catch (fishing loot only).</summary>
    public int FishingPower { get; set; }

    /// <summary>Liquid the catch comes from ("water", "honey", "lava"; fishing loot only).</summary>
    public string FishingLiquid { get; set; } = "";

    /// <summary>Quest fish the Angler wants today ("" when none; fishing loot only).</summary>
    public string AnglerQuest { get; set; } = "";

    /// <summary>How far out the position lies: 0 at the world's centre, 1 at the edge of its land (outer thirds ≥ 0.66).</summary>
    public float CentreDistance { get; set; }

    public Pcg32 Random;

    public bool HasBiome(string id)
    {
        foreach (var b in Biomes)
            if (b == id) return true;
        return false;
    }
}
