using System.Numerics;
using Strata.Core.Common;
using Strata.Core.Gameplay.Items;
using Strata.Core.Gameplay.Player;
using Strata.Core.World;

namespace Strata.Core.Session;

public sealed partial class GameWorld
{
    /// <summary>Puts a character into this world, with the accessory slots the world's difficulty unlocks (and coins that convert).</summary>
    public PlayerEntity AddPlayer(PlayerState state, Vector3 feet)
    {
        state.Inventory.AccessorySlotsUnlocked = AccessorySlotsFor(Session.Settings.Difficulty);
        state.Inventory.Content ??= Session.Content;
        state.Journal.Bestiary = Journal.Bestiary;
        var p = new PlayerEntity(state) { Position = feet };
        Players.Add(p);
        return p;
    }

    /// <summary>Accessory slots a world grants, as in the reference game: 5, a 6th in Expert and a 7th in Master (Journey has 5).</summary>
    public static int AccessorySlotsFor(Difficulty difficulty) => difficulty switch
    {
        Difficulty.Expert => PlayerInventory.BaseAccessorySlots + 1,
        Difficulty.Master => PlayerInventory.BaseAccessorySlots + 2,
        _ => PlayerInventory.BaseAccessorySlots,
    };

    /// <summary>Respawn position in meters (bed spawn if set and valid, else world spawn).</summary>
    public Vector3 SpawnPositionFor(PlayerState state)
    {
        var cell = Session.SpawnCell;
        if (state.SpawnPoints.TryGetValue(Session.Settings.Name, out var bed) && bed.Length == 3) cell = new Int3(bed[0], bed[1], bed[2]);
        cell = Session.FindStandableNear(cell);
        return new Vector3(cell.X + 0.5f, cell.Y, cell.Z + 0.5f) * Units.CellSize;
    }
}
