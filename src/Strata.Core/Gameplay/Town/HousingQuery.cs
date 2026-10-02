using Strata.Core.Gameplay.Housing;

namespace Strata.Core.Gameplay.Town;

/// <summary>The answer to a housing query: the room, whether it is a suitable home, the message and who lives there.</summary>
public sealed record HousingQuery(Room Room, bool Suitable, string Message, TownNpc? Occupant);

public enum AssignResult
{
    Assigned,

    /// <summary>The room's occupant moved into the assigned resident's old home (or became homeless).</summary>
    Swapped,
    NotSuitable,

    /// <summary>Landmark keepers, bound NPCs (and later visitors and pets) do not live in houses.</summary>
    NotAResident,
    AlreadyHome,
}
