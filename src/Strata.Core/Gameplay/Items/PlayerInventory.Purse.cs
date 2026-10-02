using Strata.Core.Content;
using Strata.Core.Content.Defs;
using CoinMath = Strata.Core.Gameplay.Items.Coins;

namespace Strata.Core.Gameplay.Items;

/// <summary>Where picked-up items land and where a shot finds its ammo, as in the reference game.</summary>
public sealed partial class PlayerInventory
{
    /// <summary>
    /// The content coins convert with (100 copper become 1 silver); set when the character is created, loaded or enters
    /// a world. Without it coins still go to the coin slots but stay as they are.
    /// </summary>
    public ContentDatabase? Content { get; set; }

    /// <summary>
    /// Picks up a stack and returns what did not fit: coins go to the coin slots (and convert), ammunition to the ammo
    /// slots, everything else (and whatever those slots cannot take) to the backpack, hotbar first.
    /// </summary>
    public ItemStack Pickup(ItemStack stack)
    {
        if (stack.IsEmpty) return ItemStack.Empty;
        if (CoinMath.IsCoin(stack)) stack = PickupCoins(stack);
        else if (FitsAmmoSlot(stack.Item!)) stack = Ammo.Add(stack);
        return Backpack.Add(stack);
    }

    /// <summary>
    /// Ammunition proper (category ammo with an ammo class) fills the ammo slots. Materials some weapon also fires
    /// (Fallen Stars for the Star Cannon) stay in the backpack, as the reference game's "not ammo" items do.
    /// </summary>
    public static bool FitsAmmoSlot(ItemDef item) => item.AmmoClass != null && item.Category == ItemCategory.Ammo;

    /// <summary>Would <see cref="Pickup"/> take the whole stack? (Coins turning into fewer coins only make more room.)</summary>
    public bool CanTake(ItemStack stack)
    {
        if (stack.IsEmpty) return true;
        long space = SpaceFor(Backpack, stack);
        if (CoinMath.IsCoin(stack)) space += SpaceFor(Coins, stack);
        else if (FitsAmmoSlot(stack.Item!)) space += SpaceFor(Ammo, stack);
        return space >= stack.Count;
    }

    /// <summary>The stack a weapon of the ammo class fires next: the ammo slots top to bottom, then the backpack; null when out.</summary>
    public (Inventory Inv, int Slot)? FindAmmo(string ammoClass)
    {
        for (int i = 0; i < Ammo.Size; i++)
            if (Ammo[i].Item?.AmmoClass == ammoClass) return (Ammo, i);
        for (int i = 0; i < Backpack.Size; i++)
            if (Backpack[i].Item?.AmmoClass == ammoClass) return (Backpack, i);
        return null;
    }

    /// <summary>Every round of the ammo class carried (the count a ranged weapon's hotbar slot shows).</summary>
    public int CountAmmo(string ammoClass) => CountClass(Ammo, ammoClass) + CountClass(Backpack, ammoClass);

    /// <summary>
    /// Merges coins into the coin slots, letting each 100 turn into 1 of the next, which frees room for the rest of the
    /// stack; returns what the coin slots could not take.
    /// </summary>
    private ItemStack PickupCoins(ItemStack stack)
    {
        var rest = Coins.Add(stack);
        while (Content != null && CoinMath.Normalize(this, Content) && !rest.IsEmpty)
        {
            int before = rest.Count;
            rest = Coins.Add(rest);
            if (rest.Count == before) break;
        }
        return rest;
    }

    private static long SpaceFor(Inventory inv, ItemStack stack)
    {
        long space = 0;
        for (int i = 0; i < inv.Size; i++)
        {
            var s = inv[i];
            if (s.IsEmpty) space += stack.Item!.MaxStack;
            else if (s.CanStackWith(stack)) space += s.SpaceLeft;
        }
        return space;
    }

    private static int CountClass(Inventory inv, string ammoClass)
    {
        int n = 0;
        for (int i = 0; i < inv.Size; i++)
            if (inv[i].Item?.AmmoClass == ammoClass) n += inv[i].Count;
        return n;
    }
}
