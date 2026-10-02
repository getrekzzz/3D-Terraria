using Strata.Core.Content.Defs;

namespace Strata.Core.Gameplay.Items;

/// <summary>The misc equipment row in the reference game's order; the values index <see cref="PlayerInventory.Misc"/> and <see cref="PlayerInventory.MiscDyes"/>.</summary>
public enum MiscSlot
{
    Pet, LightPet, Minecart, Mount, Hook,
}

public static class MiscSlots
{
    public const int Count = 5;

    /// <summary>The misc slot an item of this equip slot goes into, or null for armor, accessories and nothing.</summary>
    public static MiscSlot? FromEquipSlot(EquipSlot slot) =>
        slot >= EquipSlot.Pet && slot <= EquipSlot.Hook ? (MiscSlot)(slot - EquipSlot.Pet) : null;

    public static EquipSlot ToEquipSlot(this MiscSlot slot) => EquipSlot.Pet + (int)slot;
}
