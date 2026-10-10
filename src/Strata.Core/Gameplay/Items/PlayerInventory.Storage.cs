using Strata.Core.Content.Defs;

namespace Strata.Core.Gameplay.Items;

/// <summary>What a resident's service does with the item in <see cref="PlayerInventory.Service"/>.</summary>
public enum ServiceMode
{
    /// <summary>The Goblin Tinkerer re-rolls its prefix: only items that can carry one fit.</summary>
    Reforge,
}

public sealed partial class PlayerInventory
{
    /// <summary>The equipment storages (armor, accessories, vanity, dyes, misc and misc dyes); a change raises <see cref="EquipmentChanged"/>.</summary>
    public IReadOnlyList<Inventory> EquipmentStorages { get; }

    /// <summary>
    /// Everything the character carries or wears: backpack, coins, ammo, equipment and the service slot. Mediumcore
    /// and Hardcore deaths drop all of it; the trash (already thrown away) and the cursor are not included.
    /// </summary>
    public IReadOnlyList<Inventory> AllStorages { get; }

    /// <summary>How many of an item the character carries where it can be used: the backpack, coin and ammo slots.</summary>
    public int CountAll(ItemDef item) => Backpack.Count(item) + Coins.Count(item) + Ammo.Count(item);

    /// <summary>How many matching items the character carries in the backpack, coin and ammo slots.</summary>
    public int CountAll(Func<ItemDef, bool> match) => Backpack.Count(match) + Coins.Count(match) + Ammo.Count(match);

    /// <summary>
    /// Puts the service slot's item back into the storage (<see cref="Pickup"/>: coins and ammo to their slots, the rest
    /// hotbar first) when its service closes, and returns what did not fit (the caller drops it at the player's feet).
    /// </summary>
    public ItemStack StowService()
    {
        var stack = Service[0];
        if (stack.IsEmpty) return ItemStack.Empty;
        Service[0] = ItemStack.Empty;
        return Pickup(stack);
    }

    /// <summary>The storage behind a player slot area; null for <see cref="SlotArea.Container"/> and <see cref="SlotArea.Shop"/>.</summary>
    public Inventory? Resolve(SlotArea area) => area switch
    {
        SlotArea.Backpack => Backpack,
        SlotArea.Coins => Coins,
        SlotArea.Ammo => Ammo,
        SlotArea.Trash => Trash,
        SlotArea.Armor => Armor,
        SlotArea.Accessories => Accessories,
        SlotArea.VanityArmor => VanityArmor,
        SlotArea.VanityAccessories => VanityAccessories,
        SlotArea.Dyes => Dyes,
        SlotArea.Misc => Misc,
        SlotArea.MiscDyes => MiscDyes,
        SlotArea.Service => Service,
        _ => null,
    };
}
