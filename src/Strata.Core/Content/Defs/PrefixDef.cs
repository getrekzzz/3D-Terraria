namespace Strata.Core.Content.Defs;

/// <summary>A random modifier rolled on crafted/looted equipment (reforging).</summary>
public sealed class PrefixDef : ContentDef
{
    /// <summary>Item groups it may apply to: melee, ranged, magic, summon, tool, accessory, universal.</summary>
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
    public int MaxMana { get; init; }

    /// <summary>Multiplier applied to item value (computed from other stats when 0).</summary>
    public float ValueMultiplier { get; init; }
}
