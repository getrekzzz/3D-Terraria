using Strata.Core.Session;
using Strata.Core.Voxels.TileObjects;

namespace Strata.Core.Gameplay.Interaction;

public sealed partial class ObjectInteraction
{
    public const string LockedMessage = "It's locked.";

    /// <summary>A locked chest was opened with its key (sound, then the chest opens).</summary>
    public event Action<PlayerEntity, TileObjectInstance>? Unlocked;

    /// <summary>
    /// Opens a locked chest's lock: refused (with the lock's message) while its required flag is unset, otherwise
    /// with the key in the backpack, which a Golden Key lock uses up and a Shadow Key lock keeps. A chest whose
    /// definition has no lock opens freely.
    /// </summary>
    private bool Unlock(PlayerEntity p, TileObjectInstance obj)
    {
        if (obj.Def.Lock is not { } lk)
        {
            obj.Locked = false;
            return true;
        }
        if (lk.RequiresFlag is { } flag && !_world.Progression.IsSet(flag))
        {
            Refused?.Invoke(p, lk.Message ?? LockedMessage);
            return false;
        }
        bool has = lk.Consume ? TakeKey(p, lk.Key) : HasKey(p, lk.Key);
        if (!has)
        {
            Refused?.Invoke(p, LockedMessage);
            return false;
        }
        obj.Locked = false;
        MarkModified(obj);
        Unlocked?.Invoke(p, obj);
        return true;
    }

    private static bool HasKey(PlayerEntity p, string itemId)
    {
        var backpack = p.State.Inventory.Backpack;
        for (int i = 0; i < backpack.Size; i++)
            if (backpack[i].Item?.Id == itemId) return true;
        return false;
    }
}
