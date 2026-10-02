namespace Strata.Core.Simulation;

/// <summary>Real-date seasons (Terraria): Christmas from 15 to 31 December, Halloween from 10 October to 1 November.</summary>
public static class SeasonCalendar
{
    public const string Christmas = "christmas";
    public const string Halloween = "halloween";

    /// <summary>The season of a date ("" when none).</summary>
    public static string Of(DateOnly date) => date.Month switch
    {
        12 when date.Day >= 15 => Christmas,
        10 when date.Day >= 10 => Halloween,
        11 when date.Day == 1 => Halloween,
        _ => "",
    };
}
