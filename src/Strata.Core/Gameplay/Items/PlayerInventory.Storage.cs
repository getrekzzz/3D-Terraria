using Strata.Core.Content.Defs;

namespace Strata.Core.Gameplay.Items;

/// <summary>Every slot group a click can address. Container and Shop belong to the screen that opened them.</summary>
public enum SlotArea
{
    Backpack, Coins, Ammo, Trash, Armor, Accessories, VanityArmor, VanityAccessories, Dyes, Misc, MiscDyes, Service, Container, Shop,
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
