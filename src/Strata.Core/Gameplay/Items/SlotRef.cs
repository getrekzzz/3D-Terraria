using Strata.Core.Content.Defs;

namespace Strata.Core.Gameplay.Items;

/// <summary>Every slot group a click can address. Container and Shop belong to the screen that opened them.</summary>
public enum SlotArea
{
    Backpack, Coins, Ammo, Trash, Armor, Accessories, VanityArmor, VanityAccessories, Dyes, Misc, MiscDyes, Service, Container, Shop,
}

/// <summary>One slot: its area and the index inside that area's storage.</summary>
public readonly record struct SlotRef(SlotArea Area, int Index)
{
    /// <summary>Worn equipment (armor, accessories, vanity, dyes, misc row and misc dyes).</summary>
    public bool IsEquipment => Area is >= SlotArea.Armor and <= SlotArea.MiscDyes;

    public override string ToString() => $"{Area} {Index}";
}

/// <summary>Modifier keys held during a slot click.</summary>
[Flags]
public enum ClickMods
{
    None = 0,
    Shift = 1,
    Ctrl = 2,
    Alt = 4,
}

/// <summary>What a slot click did (the UI picks a sound from it).</summary>
public enum ClickOutcome
{
    None, Moved, Equipped, Opened, Trashed, Favorited, Sold, Thrown,
}

/// <summary>Which items each slot accepts, as in the reference game.</summary>
public static class SlotFilters
{
    private static readonly Func<ItemDef, bool> Nothing = _ => false;
    private static readonly Func<ItemDef, bool> CoinsOnly = item => item.HasTag("coin");
    private static readonly Func<ItemDef, bool> AmmoOnly = PlayerInventory.FitsAmmoSlot;
    private static readonly Func<ItemDef, bool> Reforgeable = Prefixes.CanHave;

    /// <summary>
    /// The accept predicate of a slot, or null when anything fits (backpack, trash, containers): coins only in the coin
    /// slots, ammunition only in the ammo slots, the matching armor piece per armor index (vanity items only in the
    /// vanity row), accessories without a duplicate in their row, dyes and the misc kind per misc index, and in the
    /// service slot what its <see cref="ServiceMode"/> works on (reforging: items that can carry a prefix). Shop slots
    /// and locked accessory rows (their vanity and dye slots too) take nothing; what is in them can be taken out.
    /// </summary>
    public static Func<ItemDef, bool>? For(SlotRef slot, PlayerInventory inv) => slot.Area switch
    {
        SlotArea.Coins => CoinsOnly,
        SlotArea.Ammo => AmmoOnly,
        SlotArea.Accessories or SlotArea.VanityAccessories when inv.IsAccessorySlotLocked(slot.Index) => Nothing,
        SlotArea.Dyes when slot.Index >= PlayerInventory.ArmorSlots && inv.IsAccessorySlotLocked(slot.Index - PlayerInventory.ArmorSlots) => Nothing,
        SlotArea.Armor => PlayerInventory.ArmorFilter(EquipSlot.Head + slot.Index),
        SlotArea.VanityArmor => PlayerInventory.ArmorFilter(EquipSlot.Head + slot.Index, vanityRow: true),
        SlotArea.Accessories => PlayerInventory.AccessoryFilter(inv.Accessories, slot.Index),
        SlotArea.VanityAccessories => PlayerInventory.AccessoryFilter(inv.VanityAccessories, slot.Index, vanityRow: true),
        SlotArea.Dyes or SlotArea.MiscDyes => PlayerInventory.DyeFilter,
        SlotArea.Misc => PlayerInventory.MiscFilter((MiscSlot)slot.Index),
        SlotArea.Service when inv.ServiceMode == ServiceMode.Reforge => Reforgeable,
        SlotArea.Shop => Nothing,
        _ => null,
    };
}
