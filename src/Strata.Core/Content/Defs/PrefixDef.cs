namespace Strata.Core.Content.Defs;

/// <summary>A random modifier rolled on crafted, looted or bought equipment (reforging); data/prefixes.json.</summary>
public sealed class PrefixDef : ContentDef
{
    /// <summary>Item groups it may apply to: universal, common, melee, ranged, magic, accessory.</summary>
    public string[] Applies { get; init; } = Array.Empty<string>();

    public float Weight { get; init; } = 1f;
    public float Damage { get; init; }
    public float UseSpeed { get; init; }
    public int Crit { get; init; }
    public float Knockback { get; init; }
    public float Size { get; init; }
    public float Velocity { get; init; }
    public float ManaCost { get; init; }
    public int Defense { get; init; }
    public float MoveSpeed { get; init; }

    /// <summary>Melee speed bonus of an accessory prefix (Wild, Rash, Intrepid, Violent).</summary>
    public float MeleeSpeed { get; init; }
    public int MaxMana { get; init; }

    /// <summary>Value multiplier before squaring (accessory value steps); computed from the weapon stats when 0.</summary>
    public float ValueMultiplier { get; init; }
}
