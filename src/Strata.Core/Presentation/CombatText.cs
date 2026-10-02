namespace Strata.Core.Presentation;

/// <summary>What a floating combat number reports; picks its colour, size and text.</summary>
public enum CombatTextKind
{
    /// <summary>A player's hit on a creature.</summary>
    Hostile,

    /// <summary>A critical hit on a creature: redder and bigger.</summary>
    Crit,

    /// <summary>Damage a player or a resident took.</summary>
    ToPlayer,

    /// <summary>Life a player regained (a potion, a crystal, life steal, the nurse).</summary>
    HealLife,

    /// <summary>Mana a player regained.</summary>
    HealMana,
}

/// <summary>
/// Terraria's floating combat text, engine-neutral: the colour and size of each kind (CombatText.DamagedHostile,
/// DamagedHostileCrit, DamagedFriendly, HealLife, HealMana) and the motion of a number over its life: a quick bounce in,
/// a rise that slows to a stop, and a fade over the second half. The game samples these every frame, so a number costs
/// no tween or allocation.
/// </summary>
public static class CombatText
{
    /// <summary>Seconds a number stays on screen.</summary>
    public const float Lifetime = 0.9f;

    /// <summary>Seconds of the pop-in, from <see cref="PopFrom"/> to full size with a slight overshoot.</summary>
    public const float PopTime = 0.08f;

    public const float PopFrom = 0.3f;

    /// <summary>Metres a number rises over its life (fast at first, slowing to a stop).</summary>
    public const float RiseHeight = 0.9f;

    /// <summary>Seconds after which a number starts to fade out.</summary>
    public const float FadeStart = 0.4f;

    public const float CritScale = 1.25f;

    /// <summary>Sideways jitter of a crit (metres either way), so repeated crits do not stack exactly.</summary>
    public const float CritJitter = 0.05f;

    /// <summary>Numbers up to this value keep their text cached, so common hits and heals allocate nothing.</summary>
    public const int CachedTexts = 1000;

    private static readonly string?[] Plain = new string?[CachedTexts];
    private static readonly string?[] Signed = new string?[CachedTexts];

    public static CombatTextKind KindOf(bool crit, bool toPlayer) => toPlayer ? CombatTextKind.ToPlayer : crit ? CombatTextKind.Crit : CombatTextKind.Hostile;

    public static CombatTextKind HealKind(bool mana) => mana ? CombatTextKind.HealMana : CombatTextKind.HealLife;

    public static Rgb Color(CombatTextKind kind) => kind switch
    {
        CombatTextKind.Crit => new Rgb(255, 100, 30),
        CombatTextKind.ToPlayer => new Rgb(255, 80, 90),
        CombatTextKind.HealLife => new Rgb(100, 255, 100),
        CombatTextKind.HealMana => new Rgb(100, 100, 255),
        _ => new Rgb(255, 160, 80),
    };

    public static float Scale(CombatTextKind kind) => kind == CombatTextKind.Crit ? CritScale : 1f;

    /// <summary>The number as shown: heals carry a plus sign ("+50"), damage is bare ("12").</summary>
    public static string Text(int amount, CombatTextKind kind)
    {
        bool heal = kind is CombatTextKind.HealLife or CombatTextKind.HealMana;
        if (amount < 0 || amount >= CachedTexts) return heal ? "+" + amount : amount.ToString();
        var cache = heal ? Signed : Plain;
        return cache[amount] ??= heal ? "+" + amount : amount.ToString();
    }

    /// <summary>Size multiplier at <paramref name="age"/> seconds: <see cref="PopFrom"/> up to 1 with a back-ease overshoot.</summary>
    public static float Pop(float age)
    {
        if (age >= PopTime) return 1f;
        float t = Math.Max(0f, age) / PopTime - 1f;
        const float c1 = 1.70158f, c3 = c1 + 1f;
        return PopFrom + (1f - PopFrom) * (1f + c3 * t * t * t + c1 * t * t);
    }

    /// <summary>Height above the start at <paramref name="age"/> seconds; the rising speed falls linearly to zero.</summary>
    public static float Rise(float age)
    {
        float t = Math.Clamp(age / Lifetime, 0f, 1f);
        return RiseHeight * (1f - (1f - t) * (1f - t));
    }

    /// <summary>Opacity at <paramref name="age"/> seconds: full until <see cref="FadeStart"/>, then down to 0 at <see cref="Lifetime"/>.</summary>
    public static float Alpha(float age) => age <= FadeStart ? 1f : Math.Clamp(1f - (age - FadeStart) / (Lifetime - FadeStart), 0f, 1f);
}
