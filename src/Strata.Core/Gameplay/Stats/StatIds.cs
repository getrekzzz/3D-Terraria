namespace Strata.Core.Gameplay.Stats;

/// <summary>
/// Canonical stat identifiers used by equipment/buff effects and the combat model.
/// Percentage stats are fractions (0.1 = +10 %).
/// </summary>
public static class StatIds
{
    public const string MaxLife = "maxLife";
    public const string MaxMana = "maxMana";
    public const string Defense = "defense";
    public const string LifeRegen = "lifeRegen";
    public const string ManaRegen = "manaRegen";
    public const string MoveSpeed = "moveSpeed";
    public const string Acceleration = "acceleration";
    public const string JumpHeight = "jumpHeight";
    public const string GenericDamage = "genericDamage";
    public const string MeleeDamage = "meleeDamage";
    public const string RangedDamage = "rangedDamage";
    public const string MagicDamage = "magicDamage";
    public const string SummonDamage = "summonDamage";
    public const string GenericCrit = "genericCrit";
    public const string MeleeCrit = "meleeCrit";
    public const string RangedCrit = "rangedCrit";
    public const string MagicCrit = "magicCrit";
    public const string MeleeSpeed = "meleeSpeed";
    public const string MiningSpeed = "miningSpeed";
    public const string KnockbackResist = "knockbackResist";
    public const string ManaCost = "manaCost";
    public const string MaxMinions = "maxMinions";
    public const string Reach = "reach";
    public const string EnduranceReduction = "endurance";
    public const string FlightTime = "flightTime";

    /// <summary>Extra invincibility after being hit (ticks; the Cross Necklace).</summary>
    public const string Immunity = "immunity";

    /// <summary>Swimming speed bonus (Flipper).</summary>
    public const string SwimSpeed = "swimSpeed";

    private static readonly HashSet<string> Known = new(StringComparer.Ordinal)
    {
        MaxLife, MaxMana, Defense, LifeRegen, ManaRegen, MoveSpeed, Acceleration, JumpHeight, GenericDamage,
        MeleeDamage, RangedDamage, MagicDamage, SummonDamage, GenericCrit, MeleeCrit, RangedCrit, MagicCrit,
        MeleeSpeed, MiningSpeed, KnockbackResist, ManaCost, MaxMinions, Reach, EnduranceReduction, FlightTime,
        Immunity, SwimSpeed,
    };

    public static bool IsKnown(string id) => Known.Contains(id);

    public static IReadOnlyCollection<string> All => Known;
}
