namespace Strata.Core.Gameplay.Items;

/// <summary>
/// The reference game's accelerating repeat for a held button: right-click taking one at a time, hold-to-craft and
/// hold-to-buy. The first repeat waits half a second and each later one waits a quarter less, down to every tick.
/// </summary>
public static class HoldRepeat
{
    public const int FirstIntervalTicks = 30;
    public const float Speedup = 0.75f;

    /// <summary>Ticks to wait before the next repeat after <paramref name="repeats"/> repeats so far: 30, 22, 17, 13 … 1.</summary>
    public static int IntervalTicks(int repeats) =>
        repeats >= 24 ? 1 : Math.Max(1, (int)MathF.Round(FirstIntervalTicks * MathF.Pow(Speedup, Math.Max(0, repeats))));
}
