using Strata.Core.Content.Defs;

namespace Strata.Core.Gameplay.Items;

/// <summary>The player's storage: 50-slot backpack (first 10 are the hotbar), equipment and a cursor stack.</summary>
public sealed class PlayerInventory
{
    public const int HotbarSize = 10;
    public const int BackpackSize = 50;
    public const int ArmorSlots = 3;
    public const int AccessorySlots = 5;

    public Inventory Backpack { get; } = new(BackpackSize);

    /// <summary>Head, body, legs.</summary>
    public Inventory Armor { get; } = new(ArmorSlots);

    public Inventory Accessories { get; } = new(AccessorySlots);

    /// <summary>Stack held by the mouse cursor while rearranging.</summary>
    public ItemStack Cursor { get; set; }

    public int SelectedHotbar { get; private set; }

    public ItemStack Held => Backpack[SelectedHotbar];

    public event Action? SelectionChanged;

    public void Select(int slot)
    {
        SelectedHotbar = ((slot % HotbarSize) + HotbarSize) % HotbarSize;
        SelectionChanged?.Invoke();
    }

    /// <summary>Picks up an item into the backpack (hotbar first). Returns the remainder.</summary>
    public ItemStack Pickup(ItemStack stack) => Backpack.Add(stack);

    /// <summary>Equips the item in a backpack slot into the matching equipment slot (swapping).</summary>
    public bool Equip(int backpackSlot)
    {
        var s = Backpack[backpackSlot];
        if (s.IsEmpty) return false;
        switch (s.Item!.EquipSlot)
        {
            case EquipSlot.Head: Inventory.Transfer(Backpack, backpackSlot, Armor, 0); return true;
            case EquipSlot.Body: Inventory.Transfer(Backpack, backpackSlot, Armor, 1); return true;
            case EquipSlot.Legs: Inventory.Transfer(Backpack, backpackSlot, Armor, 2); return true;
            case EquipSlot.Accessory:
                for (int i = 0; i < AccessorySlots; i++)
                    if (Accessories[i].IsEmpty || Accessories[i].Item == s.Item)
                    {
                        if (Accessories[i].Item == s.Item) return false; // no duplicate accessories
                        Inventory.Transfer(Backpack, backpackSlot, Accessories, i);
                        return true;
                    }
                Inventory.Transfer(Backpack, backpackSlot, Accessories, 0);
                return true;
            default:
                return false;
        }
    }

    public IEnumerable<ItemDef> EquippedItems =>
        Armor.Stacks.Concat(Accessories.Stacks).Select(s => s.Item!);
}
