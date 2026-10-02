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

    public static void Register(SpecCatalog c)
    {
        c.Register<HealServiceParams>(SpecKind.TownService, Heal);
        c.Register<CraftHelpParams>(SpecKind.TownService, CraftHelp);
        c.Register<SummonServiceParams>(SpecKind.TownService, SummonBoss);
        c.Register<ReforgeServiceParams>(SpecKind.TownService, Reforge);
        c.Register<TaxServiceParams>(SpecKind.TownService, Taxes);
        c.Register<AnglerQuestParams>(SpecKind.TownService, AnglerQuest);
        c.Register<AdviceParams>(SpecKind.TownService, Advice);
    }
}

/// <summary>Restores life (and removes debuffs) for coins, priced per missing life point.</summary>
public sealed class HealServiceParams : SpecParams, IValidatableSpec
{
    /// <summary>Copper per missing life point.</summary>
    public int CostPerLife { get; init; } = 15;

    public bool ClearsDebuffs { get; init; } = true;

    public string? Validate() => CostPerLife < 0 ? "costPerLife must be >= 0" : null;
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
    public string? Validate() => Fish.Length == 0 ? "needs at least one fish" : null;
}

public sealed class AnglerReward
{
    public string Item { get; init; } = "";
    public int Count { get; init; } = 1;
    public float Chance { get; init; } = 1f;
}
