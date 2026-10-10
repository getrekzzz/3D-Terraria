using System.Numerics;
using Strata.Core.Common;
using Strata.Core.Gameplay.Items;
using Strata.Core.Gameplay.Player;
using Strata.Core.World;

namespace Strata.Core.Session;

public sealed partial class GameWorld
{
    /// <summary>Puts a character into this world, with the accessory slots the world and the character unlock (and coins that convert).</summary>
    public PlayerEntity AddPlayer(PlayerState state, Vector3 feet)
    {
        UnlockAccessorySlots(state);
        state.Inventory.Content ??= Session.Content;
        state.Journal.Bestiary = Journal.Bestiary;
        var p = new PlayerEntity(state) { Position = feet };
        Players.Add(p);
        return p;
    }

    /// <summary>
    /// Accessory slots a character has in a world, as in the reference game: 5 in Classic and Journey worlds, 6 in
    /// Master; a consumed Demon Heart (<paramref name="demonHeart"/>) adds one more in Expert and Master worlds only
    /// (it stays greyed out in a Classic world).
    /// </summary>
    public static int AccessorySlotsFor(Difficulty difficulty, bool demonHeart = false) => difficulty switch
    {
        Difficulty.Expert => PlayerInventory.BaseAccessorySlots + (demonHeart ? 1 : 0),
        Difficulty.Master => PlayerInventory.BaseAccessorySlots + 1 + (demonHeart ? 1 : 0),
        _ => PlayerInventory.BaseAccessorySlots,
    };

    /// <summary>
    /// Why accessory slot <paramref name="slot"/> (0-based) is locked in a world of <paramref name="difficulty"/>, in the
    /// reference game's words: the sixth wants Expert Mode and a Demon Heart, the seventh Master Mode (and, there, the
    /// Demon Heart). Null when the slot is open.
    /// </summary>
    public static string? AccessorySlotRequirement(Difficulty difficulty, bool demonHeart, int slot)
    {
        if (slot < AccessorySlotsFor(difficulty, demonHeart)) return null;
        bool expert = difficulty is Difficulty.Expert or Difficulty.Master;
        if (slot <= PlayerInventory.BaseAccessorySlots) return expert ? "Requires a Demon Heart" : "Requires Expert Mode and a Demon Heart";
        return difficulty == Difficulty.Master ? "Requires a Demon Heart" : "Requires Master Mode";
    }

    /// <summary>
    /// Sets the accessory slots <paramref name="state"/> may use in this world (on joining, and as soon as a Demon Heart
    /// is consumed). Items in a slot that locks stay there, greyed out, and can still be taken out.
    /// </summary>
    public void UnlockAccessorySlots(PlayerState state) =>
        state.Inventory.AccessorySlotsUnlocked = AccessorySlotsFor(Session.Settings.EffectiveDifficulty, state.ExtraAccessorySlot);

    /// <summary>Respawn position in meters (bed spawn if set and valid, else world spawn).</summary>
    public Vector3 SpawnPositionFor(PlayerState state)
    {
        var cell = Session.SpawnCell;
        if (state.SpawnPoints.TryGetValue(Session.Settings.Name, out var bed) && bed.Length == 3) cell = new Int3(bed[0], bed[1], bed[2]);
        cell = Session.FindStandableNear(cell);
        return new Vector3(cell.X + 0.5f, cell.Y, cell.Z + 0.5f) * Units.CellSize;
    }
}
