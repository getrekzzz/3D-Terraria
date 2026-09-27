using Strata.Core.Common;

namespace Strata.Core.Session;

public sealed partial class GameWorld
{
    /// <summary>Tile object left where a player dies (enough of them together make a Graveyard).</summary>
    public const string TombstoneObject = "tombstone";

    /// <summary>Leaves a tombstone at (or just above) the spot where a player died, when there is room for one.</summary>
    private void DropTombstone(PlayerEntity p)
    {
        if (!Session.Content.TileObjects.TryGet(TombstoneObject, out var def)) return;
        var feet = p.FeetCell;
        for (int dy = 0; dy < 4; dy++)
            if (Editor.PlaceObject(def, feet + new Int3(0, dy, 0), p.FacingQuarter) == Gameplay.Building.PlacementResult.Ok) return;
    }
}
