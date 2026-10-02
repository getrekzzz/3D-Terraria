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

    /// <summary>Sink through blocks until clear of them (the Shimmer's phase).</summary>
    public const string Phase = "phase";

    /// <summary>Placed blocks are painted with the first paint in the backpack (Paint Sprayer).</summary>
    public const string AutoPaint = "autoPaint";

    /// <summary>Wings and rocket boots: rise while jump is held, for a while.</summary>
    public const string Flight = "flight";

    /// <summary>Double-tap a direction to dash (Tabi, Shield of Cthulhu).</summary>
    public const string Dash = "dash";

    /// <summary>Creatures with a tag stop hurting the wearer (Royal Gel: slimes).</summary>
    public const string Pacify = "pacify";

    /// <summary>Melee hits may inflict a debuff (Magma Stone: fire).</summary>
    public const string Inflict = "inflict";

    /// <summary>An information readout on the HUD (Depth Meter: depth, Compass: position, GPS: both and time).</summary>
    public const string Info = "info";

    /// <summary>Movement keys are reversed (Confused): strafe always, forward/back unless <c>forward</c> is false.</summary>
    public const string InvertControls = "invertControls";

    /// <summary>Throws a projectile along the aim with weapon attacks, on its own cooldown (Bone Glove).</summary>
    public const string AttackProjectile = "attackProjectile";

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
        c.Register<NoFallDamageParams>(SpecKind.Effect, Phase);
        c.Register<NoFallDamageParams>(SpecKind.Effect, AutoPaint);
        c.Register<FlightParams>(SpecKind.Effect, Flight);
        c.Register<DashParams>(SpecKind.Effect, Dash);
        c.Register<PacifyParams>(SpecKind.Effect, Pacify);
        c.Register<InflictParams>(SpecKind.Effect, Inflict);
        c.Register<InfoParams>(SpecKind.Effect, Info);
        c.Register<InvertControlsParams>(SpecKind.Effect, InvertControls);
        c.Register<AttackProjectileParams>(SpecKind.Effect, AttackProjectile);
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

public sealed class InflictParams : SpecParams
{
    public string Buff { get; init; } = "";
    public int Ticks { get; init; } = 180;
    public float Chance { get; init; } = 0.33f;
}

public sealed class InfoParams : SpecParams, IValidatableSpec
{
    public static readonly string[] Kinds = { "depth", "position", "time", "ores", "night_vision" };

    public string[] Show { get; init; } = Array.Empty<string>();

    public string? Validate() => Show.All(k => Array.IndexOf(Kinds, k) >= 0) ? null : $"show must be of {string.Join(", ", Kinds)}";
}

public sealed class InvertControlsParams : SpecParams
{
    /// <summary>Also reverse forward and back (Terraria's 2D confusion only swaps left and right).</summary>
    public bool Forward { get; init; } = true;
}

public sealed class AttackProjectileParams : SpecParams, IValidatableSpec
{
    public string Projectile { get; init; } = "";
    public int Damage { get; init; } = 10;
    public float Knockback { get; init; } = 3f;

    /// <summary>Ticks before the next throw (shared by every attack projectile the wearer has).</summary>
    public int Cooldown { get; init; } = 60;

    /// <summary>Launch speed (m/s).</summary>
    public float Speed { get; init; } = 14f;

    public string? Validate() => Projectile.Length == 0 ? "projectile is required" : Cooldown <= 0 ? "cooldown must be > 0" : null;
}
