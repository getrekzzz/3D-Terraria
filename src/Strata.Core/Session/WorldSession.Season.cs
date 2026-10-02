using Strata.Core.Simulation;

namespace Strata.Core.Session;

public sealed partial class WorldSession
{
    /// <summary>
    /// The current season ("christmas", "halloween" or "" for none): forced per world (the season command), otherwise
    /// from the real date. Never used by world generation.
    /// </summary>
    public string Season => Meta.ForcedSeason ?? SeasonCalendar.Of(DateOnly.FromDateTime(DateTime.Now));
}
