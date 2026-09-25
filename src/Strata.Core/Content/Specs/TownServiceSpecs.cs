namespace Strata.Core.Content.Specs;

/// <summary>Services a town resident offers in conversation besides the shop.</summary>
public static class TownServiceSpecs
{
    public const string Heal = "heal";
    public const string CraftHelp = "craftHelp";

    public static void Register(SpecCatalog c)
    {
        c.Register<HealServiceParams>(SpecKind.TownService, Heal);
        c.Register<CraftHelpParams>(SpecKind.TownService, CraftHelp);
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
