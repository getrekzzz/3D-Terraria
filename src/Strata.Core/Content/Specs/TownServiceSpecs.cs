namespace Strata.Core.Content.Specs;

/// <summary>Services a town resident offers in conversation besides the shop.</summary>
public static class TownServiceSpecs
{
    public const string Heal = "heal";
    public const string CraftHelp = "craftHelp";
    public const string SummonBoss = "summonBoss";
    public const string Reforge = "reforge";
    public const string Taxes = "taxes";
    public const string AnglerQuest = "anglerQuest";
    public const string Advice = "advice";
    public const string WorldStatus = "worldStatus";
    public const string Haircut = "haircut";
    public const string DyeTrade = "dyeTrade";

    public static void Register(SpecCatalog c)
    {
        c.Register<HealServiceParams>(SpecKind.TownService, Heal);
        c.Register<CraftHelpParams>(SpecKind.TownService, CraftHelp);
        c.Register<SummonServiceParams>(SpecKind.TownService, SummonBoss);
        c.Register<ReforgeServiceParams>(SpecKind.TownService, Reforge);
        c.Register<TaxServiceParams>(SpecKind.TownService, Taxes);
        c.Register<AnglerQuestParams>(SpecKind.TownService, AnglerQuest);
        c.Register<AdviceParams>(SpecKind.TownService, Advice);
        c.Register<WorldStatusParams>(SpecKind.TownService, WorldStatus);
        c.Register<HaircutParams>(SpecKind.TownService, Haircut);
        c.Register<DyeTradeParams>(SpecKind.TownService, DyeTrade);
    }
}

/// <summary>
/// Restores life (and removes debuffs) for coins (the Nurse): <see cref="CostPerLife"/> per missing life point plus
/// <see cref="PerDebuff"/> per debuff, times the first progression tier whose flag is set (strongest first), times
/// <see cref="ExpertMultiplier"/> in Expert and Master worlds, times the happiness price factor.
/// </summary>
public sealed class HealServiceParams : SpecParams, IValidatableSpec
{
    /// <summary>Copper per missing life point before the tier multiplier.</summary>
    public int CostPerLife { get; init; } = 15;

    /// <summary>Copper per removed debuff before the tier multiplier (default: ten life points' worth).</summary>
    public int? PerDebuff { get; init; }

    public bool ClearsDebuffs { get; init; } = true;

    /// <summary>Progression multipliers, strongest first; the first whose flag is set applies (none = ×1).</summary>
    public HealTier[] Tiers { get; init; } = Array.Empty<HealTier>();

    public float ExpertMultiplier { get; init; } = 1f;

    public string? Validate() =>
        CostPerLife < 0 || PerDebuff is < 0 ? "costPerLife and perDebuff must be >= 0"
        : ExpertMultiplier <= 0 || Array.Exists(Tiers, t => t.Multiplier <= 0 || string.IsNullOrEmpty(t.Flag)) ? "tiers need a flag and multipliers must be > 0"
        : null;
}

public sealed class HealTier
{
    public string Flag { get; init; } = "";
    public float Multiplier { get; init; } = 1f;
}

/// <summary>Reports how pure, evil and hallowed the world is (the Dryad's Status), sampled from its biomes.</summary>
public sealed class WorldStatusParams : SpecParams, IValidatableSpec
{
    public string[] EvilBiomes { get; init; } = { "corruption", "crimson" };
    public string[] HallowBiomes { get; init; } = { "hallow" };

    /// <summary>Placeholders: {world}, {pure}, {evil}, {evilName}, {hallow}.</summary>
    public string Text { get; init; } = "{world} is {pure}% pure, {evil}% {evilName} and {hallow}% hallowed.";

    public string? Validate() => string.IsNullOrWhiteSpace(Text) ? "text is required" : null;
}

/// <summary>The Guide's Help: what to do next (<c>GameWorld.Advisor.Advice</c>, from data/guide/steps.json).</summary>
public sealed class AdviceParams : SpecParams
{
    /// <summary>Button text in conversation.</summary>
    public string Label { get; init; } = "Help";
}

/// <summary>Shows what the held item can be crafted into (the guide's help).</summary>
public sealed class CraftHelpParams : SpecParams
{
}

/// <summary>
/// Calls a boss out on the player's request (the crypt keeper's curse) when the conditions hold. A resident that
/// <see cref="Transforms"/> is consumed by it and only returns after the respawn delay.
/// </summary>
public sealed class SummonServiceParams : SpecParams, IValidatableSpec
{
    public string Enemy { get; init; } = "";

    /// <summary>Button text in conversation.</summary>
    public string Label { get; init; } = "Summon";

    /// <summary>Conditions evaluated around the resident (e.g. night).</summary>
    public TypedSpec[] Conditions { get; init; } = Array.Empty<TypedSpec>();

    public string? FailMessage { get; init; }

    /// <summary>Metres above the resident where the boss appears.</summary>
    public float Height { get; init; } = 6f;

    public bool Transforms { get; init; } = true;

    public string? Validate() => string.IsNullOrWhiteSpace(Enemy) ? "enemy is required" : Height < 0 ? "height must be >= 0" : null;
}

/// <summary>Re-rolls the held weapon's, tool's or accessory's prefix for a fee (the Goblin Tinkerer).</summary>
public sealed class ReforgeServiceParams : SpecParams, IValidatableSpec
{
    /// <summary>Fee as a fraction of the item's value.</summary>
    public float CostFactor { get; init; } = 0.33f;
    public string? Validate() => CostFactor < 0 ? "costFactor must be >= 0" : null;
}

/// <summary>Collects rent from every housed resident while the player is away (the Tax Collector).</summary>
public sealed class TaxServiceParams : SpecParams, IValidatableSpec
{
    /// <summary>Copper per housed resident per in-game hour.</summary>
    public int PerResidentHour { get; init; } = 50;

    /// <summary>Most that can pile up (copper).</summary>
    public long Cap { get; init; } = 100000;
    public string? Validate() => PerResidentHour < 0 || Cap < 0 ? "perResidentHour and cap must be >= 0" : null;
}

/// <summary>A daily fishing quest: bring today's fish for coins and a reward (the Angler).</summary>
public sealed class AnglerQuestParams : SpecParams, IValidatableSpec
{
    /// <summary>Fish asked for, one per day in turn.</summary>
    public string[] Fish { get; init; } = Array.Empty<string>();

    /// <summary>Rewards; one is given per completed quest (weighted by chance, in order).</summary>
    public AnglerReward[] Rewards { get; init; } = Array.Empty<AnglerReward>();

    /// <summary>Coins paid per quest [min, max] (copper).</summary>
    public long[] Coins { get; init; } = { 2000, 10000 };

    /// <summary>Coins grow by this fraction per completed quest, up to <see cref="CoinQuestCap"/> quests.</summary>
    public float CoinGrowth { get; init; } = 0.02f;
    public int CoinQuestCap { get; init; } = 50;
    public string? Validate() => Fish.Length == 0 ? "needs at least one fish" : null;
}

public sealed class AnglerReward
{
    public string Item { get; init; } = "";
    public int Count { get; init; } = 1;
    public float Chance { get; init; } = 1f;

    /// <summary>Offered only once the player has completed at least this many quests.</summary>
    public int MinQuests { get; init; }

    /// <summary>When above 0: given for certain (besides the rolled reward) on every quest count that is a multiple of
    /// it, from <see cref="MinQuests"/> on (the reference game's milestone rewards).</summary>
    public int EveryQuests { get; init; }
}
