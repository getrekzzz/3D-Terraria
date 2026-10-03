using Strata.Core.Gameplay.Housing;

namespace Strata.Core.Session;

public sealed partial class GameWorld
{
    private HousingCache? _rooms;

    /// <summary>Scanned rooms with edit invalidation (town checks, housing queries), created on first use.</summary>
    public HousingCache Rooms => _rooms ??= new HousingCache(Housing, Session.World, () => Session.Clock.Tick);
}
