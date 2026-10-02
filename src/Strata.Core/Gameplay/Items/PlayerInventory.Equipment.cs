using Strata.Core.Content.Defs;

namespace Strata.Core.Gameplay.Items;

public sealed partial class PlayerInventory
{
    private int _accessorySlotsUnlocked = BaseAccessorySlots;
    private int _hiddenAccessories;
    private int _nextAccessorySwap;
    private int _nextVanitySwap;
    private int _nextDyeSwap;

    /// <summary>Raised when anything worn changes: armor, accessories, vanity, dyes, misc, unlocked or hidden slots.</summary>
    public event Action? EquipmentChanged;

    /// <summary>
    /// Accessory slots that count (5–7, set by the world's difficulty). Items left in a locked slot stay there but grant
    /// nothing until the character plays a world that unlocks it again.
    /// </summary>
    public int AccessorySlotsUnlocked
    {
        get => _accessorySlotsUnlocked;
        set
        {
            int v = Math.Clamp(value, BaseAccessorySlots, MaxAccessorySlots);
            if (v == _accessorySlotsUnlocked) return;
            _accessorySlotsUnlocked = v;
            EquipmentChanged?.Invoke();
        }
    }

    /// <summary>Accessory slots whose item is not drawn on the character (bit i = slot i); they still grant their effects.</summary>
    public int HiddenAccessories
    {
        get => _hiddenAccessories;
        set
        {
            int v = value & ((1 << MaxAccessorySlots) - 1);
            if (v == _hiddenAccessories) return;
            _hiddenAccessories = v;
            EquipmentChanged?.Invoke();
        }
    }

    public bool IsAccessoryHidden(int slot) => (_hiddenAccessories & (1 << slot)) != 0;

    public void SetAccessoryHidden(int slot, bool hidden) =>
        HiddenAccessories = hidden ? _hiddenAccessories | (1 << slot) : _hiddenAccessories & ~(1 << slot);

    /// <summary>The stacks that grant stats: the armor and the unlocked accessory slots. Vanity, dyes and misc grant nothing.</summary>
    public IEnumerable<ItemStack> EquippedStacks
    {
        get
        {
            for (int i = 0; i < ArmorSlots; i++)
                if (!Armor[i].IsEmpty) yield return Armor[i];
            for (int i = 0; i < _accessorySlotsUnlocked; i++)
                if (!Accessories[i].IsEmpty) yield return Accessories[i];
        }
    }

    public IEnumerable<ItemDef> EquippedItems => EquippedStacks.Select(s => s.Item!);

    public ItemDef? EquippedPet => Misc[(int)MiscSlot.Pet].Item;
    public ItemDef? EquippedLightPet => Misc[(int)MiscSlot.LightPet].Item;
    public ItemDef? EquippedMinecart => Misc[(int)MiscSlot.Minecart].Item;
    public ItemDef? EquippedMount => Misc[(int)MiscSlot.Mount].Item;
    public ItemDef? EquippedHook => Misc[(int)MiscSlot.Hook].Item;

    /// <summary>The worn armor pieces when head, body and legs all share one armor set; otherwise empty.</summary>
    public IReadOnlyList<ItemDef> CompleteSet()
    {
        var set = Armor[0].Item?.ArmorSet;
        if (set == null) return Array.Empty<ItemDef>();
        for (int i = 1; i < ArmorSlots; i++)
            if (Armor[i].Item?.ArmorSet != set) return Array.Empty<ItemDef>();
        return new[] { Armor[0].Item!, Armor[1].Item!, Armor[2].Item! };
    }

    /// <summary>Equips the item in a backpack slot (see <see cref="EquipFrom"/>).</summary>
    public bool Equip(int backpackSlot) => EquipFrom(Backpack, backpackSlot);

    /// <summary>
    /// Wears the stack in a slot of another storage, swapping out what was there, as the reference game's equip click
    /// does: armor goes to its armor slot (vanity items to the vanity row), accessories to the first empty unlocked slot
    /// (or, when all are full, to the next one in turn), dyes to the first empty dye slot and pets, light pets,
    /// minecarts, mounts and hooks to their misc slot. Returns false when the item cannot be worn or a copy of the
    /// accessory is already worn.
    /// </summary>
    public bool EquipFrom(Inventory from, int slot)
    {
        var s = from[slot];
        if (s.IsEmpty) return false;
        var item = s.Item!;
        bool vanity = item.Category == ItemCategory.Vanity;
        switch (item.EquipSlot)
        {
            case EquipSlot.Head:
            case EquipSlot.Body:
            case EquipSlot.Legs:
                var armor = vanity ? VanityArmor : Armor;
                if (from == armor) return false;
                Inventory.Transfer(from, slot, armor, item.EquipSlot - EquipSlot.Head);
                return true;
            case EquipSlot.Accessory:
                return vanity
                    ? EquipInto(from, slot, VanityAccessories, _accessorySlotsUnlocked, ref _nextVanitySwap, noDuplicates: true)
                    : EquipInto(from, slot, Accessories, _accessorySlotsUnlocked, ref _nextAccessorySwap, noDuplicates: true);
            case EquipSlot.None:
                return item.Category == ItemCategory.Dye && EquipInto(from, slot, Dyes, ArmorSlots + _accessorySlotsUnlocked, ref _nextDyeSwap, noDuplicates: false);
            default:
                if (MiscSlots.FromEquipSlot(item.EquipSlot) is not { } misc || from == Misc) return false;
                Inventory.Transfer(from, slot, Misc, (int)misc);
                return true;
        }
    }

    private static bool EquipInto(Inventory from, int slot, Inventory row, int usable, ref int nextSwap, bool noDuplicates)
    {
        if (from == row) return false;
        var item = from[slot].Item;
        // The whole row, locked slots included (as AccessoryFilter does): a copy left in a locked slot would otherwise
        // stack its effects once a harder world unlocks that slot again.
        if (noDuplicates)
            for (int i = 0; i < row.Size; i++)
                if (row[i].Item == item) return false;
        for (int i = 0; i < usable; i++)
            if (row[i].IsEmpty)
            {
                Inventory.Transfer(from, slot, row, i);
                return true;
            }
        int target = nextSwap % usable;
        nextSwap = (target + 1) % usable;
        Inventory.Transfer(from, slot, row, target);
        return true;
    }

    /// <summary>Armor slot filter; vanity items fit only the vanity row, real armor fits both.</summary>
    public static Func<ItemDef, bool> ArmorFilter(EquipSlot slot, bool vanityRow = false) =>
        item => item.EquipSlot == slot && (vanityRow || item.Category != ItemCategory.Vanity);

    /// <summary>Accessories only (vanity accessories only in the vanity row), and never two of the same in one row.</summary>
    public static Func<ItemDef, bool> AccessoryFilter(Inventory row, int slot, bool vanityRow = false) => item =>
    {
        if (item.EquipSlot != EquipSlot.Accessory || (!vanityRow && item.Category == ItemCategory.Vanity)) return false;
        for (int i = 0; i < row.Size; i++)
            if (i != slot && row[i].Item == item) return false;
        return true;
    };

    public static readonly Func<ItemDef, bool> DyeFilter = item => item.Category == ItemCategory.Dye;

    public static Func<ItemDef, bool> MiscFilter(MiscSlot slot)
    {
        var equip = slot.ToEquipSlot();
        return item => item.EquipSlot == equip;
    }
}
