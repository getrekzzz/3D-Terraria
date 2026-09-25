namespace Strata.Core.Content.Specs;

/// <summary>Parameters for equipment/buff effects (evaluated by Strata.Core.Gameplay.Stats).</summary>
public static class EffectSpecs
{
    public const string Stat = "stat";
    public const string ExtraJump = "extraJump";
    public const string DamageOverTime = "damageOverTime";
    public const string Immune = "immune";
    public const string EmitLight = "emitLight";
    public const string NoFallDamage = "noFallDamage";

    public static void Register(SpecCatalog c)
    {
        c.Register<StatEffectParams>(SpecKind.Effect, Stat);
        c.Register<ExtraJumpParams>(SpecKind.Effect, ExtraJump);
        c.Register<DamageOverTimeParams>(SpecKind.Effect, DamageOverTime);
        c.Register<ImmuneParams>(SpecKind.Effect, Immune);
        c.Register<EmitLightParams>(SpecKind.Effect, EmitLight);
        c.Register<NoFallDamageParams>(SpecKind.Effect, NoFallDamage);
    }
}

public sealed class StatEffectParams : SpecParams, IValidatableSpec
{
    public string Stat { get; init; } = "";

    /// <summary>Additive bonus (flat for integer stats, fraction for percentage stats).</summary>
    public float Add { get; init; }

    /// <summary>Multiplicative factor (1 = no change).</summary>
    public float Mul { get; init; } = 1f;

    public string? Validate() => Gameplay.Stats.StatIds.IsKnown(Stat) ? null : $"unknown stat '{Stat}'";
}

public sealed class ExtraJumpParams : SpecParams
{
    /// <summary>Jump height in cells.</summary>
    public float Height { get; init; } = 6f;
}

public sealed class DamageOverTimeParams : SpecParams
{
    public int PerSecond { get; init; } = 2;
}

public sealed class ImmuneParams : SpecParams
{
    public string Buff { get; init; } = "";
}

public sealed class EmitLightParams : SpecParams
{
    public int[] Color { get; init; } = { 8, 8, 8 };
}

public sealed class NoFallDamageParams : SpecParams
{
}
