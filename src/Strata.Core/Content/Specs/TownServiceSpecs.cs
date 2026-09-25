namespace Strata.Core.Content.Specs;

/// <summary>Services a town resident offers in conversation besides the shop.</summary>
public static class TownServiceSpecs
{
    public const string Heal = "heal";
    public const string CraftHelp = "craftHelp";
    public const string SummonBoss = "summonBoss";

    public static void Register(SpecCatalog c)
    {
        c.Register<HealServiceParams>(SpecKind.TownService, Heal);
        c.Register<CraftHelpParams>(SpecKind.TownService, CraftHelp);
        c.Register<SummonServiceParams>(SpecKind.TownService, SummonBoss);
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
