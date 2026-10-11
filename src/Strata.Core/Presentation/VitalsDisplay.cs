namespace Strata.Core.Presentation;

/// <summary>
/// A row of HUD icons standing for one vital (hearts, mana stars, breath bubbles): how many icons, how much each one
/// holds, how many are golden (life fruit hearts) and how full icon <c>i</c> is.
/// </summary>
public readonly record struct VitalRow(int Count, float PerIcon, int Golden, float Value)
{
    /// <summary>How full icon <paramref name="i"/> is, 0–1 (icons fill left to right).</summary>
    public float Fill(int i) => PerIcon <= 0 ? 0f : Math.Clamp((Value - i * PerIcon) / PerIcon, 0f, 1f);

    /// <summary>Icons that are completely full.</summary>
    public int Full => PerIcon <= 0 ? 0 : Math.Min(Count, (int)(Value / PerIcon));
}

/// <summary>
/// Terraria's vitals maths: 20 life per heart up to 20 hearts (400 life); beyond that the 20 hearts share the
/// maximum and each life fruit (5 life above 400) turns one heart golden. Mana shows 20 per star up to 10 stars (200),
/// then the 10 stars share the maximum. Breath shows 10 bubbles.
/// </summary>
public static class VitalsDisplay
{
    public const int MaxHearts = 20;
    public const int LifePerHeart = 20;
    public const int MaxStars = 10;
    public const int ManaPerStar = 20;
    public const int BubbleCount = 10;

    /// <summary>
    /// Hearts for <paramref name="life"/> of <paramref name="maxLife"/> (buffs included); golden hearts follow the
    /// permanent maximum from crystals and fruit (<paramref name="permanentMax"/>), so Lifeforce never gilds a heart.
    /// </summary>
    public static VitalRow Hearts(int life, int maxLife, int permanentMax)
    {
        float per = maxLife > MaxHearts * LifePerHeart ? maxLife / (float)MaxHearts : LifePerHeart;
        int count = Icons(maxLife, per, MaxHearts);
        int golden = Math.Min(count, Math.Max(0, (permanentMax - MaxHearts * LifePerHeart) / 5));
        return new VitalRow(count, per, golden, Math.Max(0, life));
    }

    public static VitalRow Stars(int mana, int maxMana)
    {
        float per = maxMana > MaxStars * ManaPerStar ? maxMana / (float)MaxStars : ManaPerStar;
        return new VitalRow(Icons(maxMana, per, MaxStars), per, 0, Math.Max(0, mana));
    }

    /// <summary>Breath as 10 bubbles (shown only while it is below the maximum).</summary>
    public static VitalRow Bubbles(int breath, int maxBreath) =>
        new(BubbleCount, maxBreath / (float)BubbleCount, 0, Math.Max(0, breath));

    private static int Icons(int max, float per, int cap) => Math.Clamp((int)MathF.Ceiling(max / per - 1e-4f), max > 0 ? 1 : 0, cap);
}
