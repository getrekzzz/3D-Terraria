namespace Strata.Core.Content.Specs;

/// <summary>Creature contact and death effects (enemy <c>onContact</c> / <c>onDeath</c>).</summary>
public static class CreatureEffectSpecs
{
    public const string ApplyBuff = "applyBuff";
    public const string PourLiquid = "pourLiquid";
    public const string SpawnEnemies = "spawnEnemies";

    public static void Register(SpecCatalog c)
    {
        c.Register<ApplyBuffParams>(SpecKind.CreatureEffect, ApplyBuff);
        c.Register<PourLiquidParams>(SpecKind.CreatureEffect, PourLiquid);
        c.Register<SpawnEnemiesParams>(SpecKind.CreatureEffect, SpawnEnemies);
    }
}

/// <summary>Leaves liquid where the creature died (lava gels).</summary>
public sealed class PourLiquidParams : SpecParams, IValidatableSpec
{
    public string Liquid { get; init; } = "";

    /// <summary>Levels poured (31 = one full cell).</summary>
    public int Amount { get; init; } = 31;

    public string? Validate() => Amount is < 1 or > 31 * 8 ? "amount must be 1–248" : null;
}

/// <summary>Splits into other creatures (gels shedding smaller gels).</summary>
public sealed class SpawnEnemiesParams : SpecParams, IValidatableSpec
{
    public string Enemy { get; init; } = "";
    public int Count { get; init; } = 2;

    public string? Validate() => Count is < 1 or > 12 ? "count must be 1–12" : null;
}
