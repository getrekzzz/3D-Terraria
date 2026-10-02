namespace Strata.Core.Presentation;

/// <summary>An 8-bit RGB colour, so Core can hand colours to the engine layer without referencing it.</summary>
public readonly record struct Rgb(byte R, byte G, byte B)
{
    public override string ToString() => $"#{R:x2}{G:x2}{B:x2}";
}

/// <summary>
/// Item name colours by rarity, with Terraria's values: Gray (-1, trash), White (0), Blue (1) through Purple (11), and
/// the special tiers Quest (-11, amber), Expert (-12, a rainbow cycling through every hue) and Master (-13, fiery red
/// pulsing towards orange). The engine layer turns these into UI colours (UiTheme.Rarity).
/// </summary>
public static class RarityColors
{
    public const int Gray = -1;
    public const int White = 0;
    public const int Purple = 11;
    public const int Quest = -11;
    public const int Expert = -12;
    public const int Master = -13;

    /// <summary>Seconds per full hue cycle of the Expert rainbow (Terraria's disco colour steps 7/255 per tick at 60 Hz).</summary>
    public const double ExpertCycleSeconds = 6 * 255 / 7.0 / 60.0;

    /// <summary>Seconds per red → orange → red pulse of the Master colour.</summary>
    public const double MasterPulseSeconds = 1.6;

    private static readonly Rgb[] Tiers =
    {
        new(255, 255, 255), // 0 White
        new(150, 150, 255), // 1 Blue
        new(150, 255, 150), // 2 Green
        new(255, 200, 150), // 3 Orange
        new(255, 150, 150), // 4 Light Red
        new(255, 150, 255), // 5 Pink
        new(210, 160, 255), // 6 Light Purple
        new(150, 255, 10), //  7 Lime
        new(255, 255, 10), //  8 Yellow
        new(5, 200, 255), //   9 Cyan
        new(255, 40, 100), //  10 Red
        new(180, 40, 255), //  11 Purple
    };

    private static readonly Rgb GrayColor = new(130, 130, 130);
    private static readonly Rgb QuestColor = new(255, 175, 0);

    /// <summary>True for the tiers whose colour changes over time (Expert and Master).</summary>
    public static bool IsAnimated(int rarity) => rarity is Expert or Master;

    /// <summary>
    /// The colour of <paramref name="rarity"/> at <paramref name="seconds"/> (only Expert and Master depend on time).
    /// Tiers above 11 show as Purple, the highest; any other negative tier shows as Gray.
    /// </summary>
    public static Rgb Of(int rarity, double seconds = 0) => rarity switch
    {
        >= 0 => Tiers[Math.Min(rarity, Purple)],
        Quest => QuestColor,
        Expert => Rainbow(seconds),
        Master => MasterPulse(seconds),
        _ => GrayColor,
    };

    /// <summary>Fully saturated hue rotation: red → yellow → green → cyan → blue → magenta → red.</summary>
    private static Rgb Rainbow(double seconds)
    {
        double phase = Wrap(seconds / ExpertCycleSeconds) * 6.0;
        int sector = Math.Min((int)phase, 5);
        byte up = ToByte(phase - sector), down = ToByte(1.0 - (phase - sector));
        return sector switch
        {
            0 => new Rgb(255, up, 0),
            1 => new Rgb(down, 255, 0),
            2 => new Rgb(0, 255, up),
            3 => new Rgb(0, down, 255),
            4 => new Rgb(up, 0, 255),
            _ => new Rgb(255, 0, down),
        };
    }

    /// <summary>Red with a green channel swinging 0 → 200 → 0 (Terraria's master colour), a triangle wave.</summary>
    private static Rgb MasterPulse(double seconds)
    {
        double phase = Wrap(seconds / MasterPulseSeconds) * 2.0;
        double amount = phase <= 1.0 ? phase : 2.0 - phase;
        return new Rgb(255, (byte)Math.Round(amount * 200.0), 0);
    }

    private static double Wrap(double x)
    {
        double f = x - Math.Floor(x);
        return f >= 1.0 ? 0.0 : f;
    }

    private static byte ToByte(double unit) => (byte)Math.Round(Math.Clamp(unit, 0.0, 1.0) * 255.0);
}
