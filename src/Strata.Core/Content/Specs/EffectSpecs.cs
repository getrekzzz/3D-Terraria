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
    public const string LiquidImmune = "liquidImmune";
    public const string WaterBreathing = "waterBreathing";

    /// <summary>Held items cannot be used (the crypt's hex).</summary>
    public const string NoItemUse = "noItemUse";

    /// <summary>Wings and rocket boots: rise while jump is held, for a while.</summary>
    public const string Flight = "flight";

    /// <summary>Double-tap a direction to dash (Tabi, Shield of Cthulhu).</summary>
    public const string Dash = "dash";

    /// <summary>Creatures with a tag stop hurting the wearer (Royal Gel: slimes).</summary>
    public const string Pacify = "pacify";

    public static void Register(SpecCatalog c)
    {
        c.Register<StatEffectParams>(SpecKind.Effect, Stat);
        c.Register<ExtraJumpParams>(SpecKind.Effect, ExtraJump);
        c.Register<DamageOverTimeParams>(SpecKind.Effect, DamageOverTime);
        c.Register<ImmuneParams>(SpecKind.Effect, Immune);
        c.Register<LiquidImmuneParams>(SpecKind.Effect, LiquidImmune);
        c.Register<NoFallDamageParams>(SpecKind.Effect, WaterBreathing);
        c.Register<EmitLightParams>(SpecKind.Effect, EmitLight);
        c.Register<NoFallDamageParams>(SpecKind.Effect, NoFallDamage);
        c.Register<NoFallDamageParams>(SpecKind.Effect, NoItemUse);
        c.Register<FlightParams>(SpecKind.Effect, Flight);
        c.Register<DashParams>(SpecKind.Effect, Dash);
        c.Register<PacifyParams>(SpecKind.Effect, Pacify);
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

/// <summary>No contact damage from a liquid (lava walking).</summary>
public sealed class LiquidImmuneParams : SpecParams
{
    public string Liquid { get; init; } = "";
}

public sealed class EmitLightParams : SpecParams
{
    public int[] Color { get; init; } = { 8, 8, 8 };
}

public sealed class NoFallDamageParams : SpecParams
{
}

/// <summary>
/// Flight while jump is held in the air: wings (they also glide once the flight time is spent) or rocket boots.
/// Wings and boots stack: boots burn after the wings run out.
/// </summary>
public sealed class FlightParams : SpecParams, IValidatableSpec
{
    /// <summary>Flight time in ticks (refilled on landing).</summary>
    public int Time { get; init; } = 60;

    /// <summary>Upward acceleration (m/s²) and the fastest rise (m/s).</summary>
    public float Ascent { get; init; } = 40f;

    public float MaxRise { get; init; } = 7f;

    /// <summary>Wings glide (the slowest fall while jump is held, m/s); boots do not (0).</summary>
    public float Glide { get; init; }

    /// <summary>Horizontal speed multiplier while flying.</summary>
    public float AirSpeed { get; init; } = 1.2f;

    /// <summary>Rocket boots rather than wings.</summary>
    public bool Boots { get; init; }

    public string? Validate() => Time <= 0 ? "time must be > 0" : Ascent <= 0 ? "ascent must be > 0" : null;
}

public sealed class DashParams : SpecParams
{
    /// <summary>Dash speed (m/s) and ticks before the next dash.</summary>
    public float Speed { get; init; } = 14f;

    public int Cooldown { get; init; } = 45;
}

public sealed class PacifyParams : SpecParams
{
    /// <summary>Creature tag made harmless (e.g. "gel").</summary>
    public string Tag { get; init; } = "";
}
