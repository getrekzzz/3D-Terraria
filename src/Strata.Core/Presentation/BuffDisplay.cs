using Strata.Core.Content.Defs;

namespace Strata.Core.Presentation;

/// <summary>
/// How buffs read on the buff bar and in tooltips, as in Terraria: a short countdown under the icon ("59 s", "5 m",
/// "2 h"), a duration line on potions ("5 minute duration"), and which buffs the player may cancel or see time on.
/// </summary>
public static class BuffDisplay
{
    public const int TicksPerSecond = 60;

    /// <summary>The countdown under a buff icon: whole seconds below a minute, then whole minutes, then whole hours.</summary>
    public static string Timer(int ticks)
    {
        int seconds = Seconds(ticks);
        if (seconds < 60) return $"{seconds} s";
        if (seconds < 3600) return $"{seconds / 60} m";
        return $"{seconds / 3600} h";
    }

    /// <summary>A potion's duration line: "30 second duration", "5 minute duration", "1 hour duration".</summary>
    public static string Duration(int ticks)
    {
        int seconds = Seconds(ticks);
        if (seconds < 60) return $"{seconds} second duration";
        if (seconds < 3600) return $"{(seconds + 30) / 60} minute duration";
        return $"{(seconds + 1800) / 3600} hour duration";
    }

    /// <summary>Right-clicking the icon removes it: never for debuffs or buffs the player cannot shed.</summary>
    public static bool CanCancel(BuffDef buff) => !buff.Debuff && !buff.Persistent;

    /// <summary>Whether a countdown is drawn (aura and liquid buffs refresh constantly and hide it).</summary>
    public static bool ShowsTimer(BuffDef buff) => !buff.HideTimer && !buff.Persistent;

    /// <summary>Remaining whole seconds, rounding up so a buff never shows 0 while it lasts.</summary>
    private static int Seconds(int ticks) => Math.Max(0, (ticks + TicksPerSecond - 1) / TicksPerSecond);
}
