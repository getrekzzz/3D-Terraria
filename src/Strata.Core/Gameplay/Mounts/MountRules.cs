using Strata.Core.Content.Defs;
using Strata.Core.Session;

namespace Strata.Core.Gameplay.Mounts;

/// <summary>
/// Getting on and off mounts: using a mount item (or the quick-mount key) summons its mount, using it again (or another
/// mount's item) dismounts or swaps. Riding shows the mount's buff; a minecart and a mount exclude each other; death
/// dismounts.
/// </summary>
public static class MountRules
{
    /// <summary>Ticks the mount buff is given for (removed on dismount; it never runs out while riding).</summary>
    private const int BuffTicks = int.MaxValue / 4;

    /// <summary>Summons the mount of <paramref name="item"/>, or dismounts if it is the one being ridden. Returns whether the rider is now mounted.</summary>
    public static bool Toggle(GameWorld world, PlayerEntity rider, ItemDef item, MountParams mount)
    {
        if (rider.Mount is { } current)
        {
            Dismount(world, rider);
            if (current.ItemId == item.Id) return false;
        }
        if (!rider.Alive) return false;
        rider.Minecart = null;
        rider.Hook.Reset();
        rider.Mount = new MountRide(mount, item.Id);
        if (mount.Buff != null && world.Session.Content.Buffs.TryGet(mount.Buff, out var buff)) rider.State.Buffs.Add(buff, BuffTicks);
        return true;
    }

    public static void Dismount(GameWorld world, PlayerEntity rider)
    {
        if (rider.Mount is not { } ride) return;
        rider.Mount = null;
        if (ride.Config.Buff != null) rider.State.Buffs.Remove(ride.Config.Buff);
    }

    /// <summary>
    /// The quick-mount key: the mount in the equipment's mount slot, else the first mount item in the backpack; toggles
    /// it. Returns false when the player has no mount.
    /// </summary>
    public static bool QuickMount(GameWorld world, PlayerEntity rider)
    {
        if (rider.Mount != null)
        {
            Dismount(world, rider);
            return true;
        }
        var item = SlotMount?.Invoke(rider) ?? FirstMountItem(world, rider);
        if (item == null || world.Uses[item].Ride is not { } mount) return false;
        return Toggle(world, rider, item, mount);
    }

    /// <summary>The item in a player's mount equipment slot, when the inventory has one (hooked up by the equipment UI).</summary>
    public static Func<PlayerEntity, ItemDef?>? SlotMount { get; set; }

    private static ItemDef? FirstMountItem(GameWorld world, PlayerEntity rider)
    {
        var backpack = rider.State.Inventory.Backpack;
        for (int i = 0; i < backpack.Size; i++)
            if (backpack[i].Item is { } item && world.Uses[item].Ride != null) return item;
        return null;
    }
}
