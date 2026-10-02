namespace Strata.Core.Gameplay.Items;

/// <summary>
/// The player's storage, laid out like the reference game's: the 50-slot backpack (first 10 are the hotbar), coin and
/// ammo slots, a trash slot that is saved, the equipment (<see cref="PlayerInventory"/>.Equipment.cs) and a cursor stack.
/// Pickups, coins and ammo are routed in <see cref="PlayerInventory"/>.Purse.cs.
/// </summary>
public sealed partial class PlayerInventory
{
    public const int HotbarSize = 10;
    public const int BackpackSize = 50;
    public const int CoinSlots = 4;
    public const int AmmoSlots = 4;
    public const int ArmorSlots = 3;

    /// <summary>Accessory slots every world has; Master worlds unlock a 6th and a Demon Heart one more in Expert and Master.</summary>
    public const int BaseAccessorySlots = 5;
    public const int MaxAccessorySlots = 7;

    /// <summary>One dye per armor piece (0–2), then one per accessory slot (3–9).</summary>
    public const int DyeSlots = ArmorSlots + MaxAccessorySlots;

    public PlayerInventory()
    {
        EquipmentStorages = new[] { Armor, Accessories, VanityArmor, VanityAccessories, Dyes, Misc, MiscDyes };
        AllStorages = new[] { Backpack, Coins, Ammo, Armor, Accessories, VanityArmor, VanityAccessories, Dyes, Misc, MiscDyes, Service };
        foreach (var storage in EquipmentStorages) storage.Changed += _ => EquipmentChanged?.Invoke();
    }

    public Inventory Backpack { get; } = new(BackpackSize, keepsFavorites: true);

    public Inventory Coins { get; } = new(CoinSlots, keepsFavorites: true);

    public Inventory Ammo { get; } = new(AmmoSlots, keepsFavorites: true);

    /// <summary>The last item thrown away; it stays until something else is trashed (and survives quitting).</summary>
    public Inventory Trash { get; } = new(1);

    /// <summary>Head, body, legs.</summary>
    public Inventory Armor { get; } = new(ArmorSlots);

    /// <summary>Functional accessories; only the first <see cref="AccessorySlotsUnlocked"/> count.</summary>
    public Inventory Accessories { get; } = new(MaxAccessorySlots);

    /// <summary>Armor shown instead of the worn pieces (head, body, legs); grants nothing.</summary>
    public Inventory VanityArmor { get; } = new(ArmorSlots);

    /// <summary>Accessories worn for their looks only, one per accessory slot.</summary>
    public Inventory VanityAccessories { get; } = new(MaxAccessorySlots);

    /// <summary>Dyes for the armor pieces (0–2) and the accessory slots (3–9).</summary>
    public Inventory Dyes { get; } = new(DyeSlots);

    /// <summary>Pet, light pet, minecart, mount and hook, indexed by <see cref="MiscSlot"/>.</summary>
    public Inventory Misc { get; } = new(MiscSlots.Count);

    /// <summary>Dyes for the misc row, indexed by <see cref="MiscSlot"/>.</summary>
    public Inventory MiscDyes { get; } = new(MiscSlots.Count);

    /// <summary>The item handed to a town service (the Goblin Tinkerer's reforge slot); the service UI stows it when it closes.</summary>
    public Inventory Service { get; } = new(1);

    /// <summary>What the service slot is for while a resident's service is open; it decides what the slot accepts.</summary>
    public ServiceMode ServiceMode { get; set; } = ServiceMode.Reforge;

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
}
