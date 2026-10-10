using Strata.Core.Content;

namespace Strata.Core.Gameplay.Items;

/// <summary>Paying and receiving coins from an inventory, making change as needed.</summary>
public static class Wallet
{
    /// <summary>
    /// Pays <paramref name="copper"/> from the player's purse: the coin slots first, and the coins in the backpack too
    /// when those are not enough. The change goes back to the coin slots as the fewest coins (then the backpack);
    /// change that fits nowhere is returned for dropping. Fails (changes nothing) when poor.
    /// </summary>
    public static bool TrySpend(PlayerInventory inv, ContentDatabase content, long copper, out List<ItemStack> overflow)
    {
        overflow = new List<ItemStack>();
        if (copper < 0) throw new ArgumentOutOfRangeException(nameof(copper));
        long inSlots = Coins.Total(inv.Coins);
        long total = inSlots + Coins.Total(inv.Backpack);
        if (total < copper) return false;
        if (copper == 0) return true;
        if (inSlots < copper)
        {
            inv.Backpack.Remove(d => d.HasTag("coin"), int.MaxValue);
            inSlots = total;
        }
        Coins.Layout(inv, content, inSlots - copper, overflow);
        return true;
    }

    /// <summary>
    /// Pays as a shop does in the reference game (1.4): the purse first (<see cref="TrySpend(PlayerInventory, ContentDatabase, long, out List{ItemStack})"/>),
    /// then the coins kept in personal storage (<paramref name="savings"/>, Piggy Bank before Safe), each storage
    /// emptied of coins only while the price is still not met. The change goes to the coin slots. Fails (changes
    /// nothing) when the purse and savings together are too poor.
    /// </summary>
    public static bool TrySpend(PlayerInventory inv, IEnumerable<Inventory> savings, ContentDatabase content, long copper, out List<ItemStack> overflow)
    {
        if (copper < 0) throw new ArgumentOutOfRangeException(nameof(copper));
        long purse = Coins.Total(inv);
        if (purse >= copper) return TrySpend(inv, content, copper, out overflow);
        overflow = new List<ItemStack>();
        var banks = savings.ToList();
        if (purse + banks.Sum(Coins.Total) < copper) return false;
        long need = copper - purse, taken = 0;
        foreach (var bank in banks)
        {
            if (taken >= need) break;
            long held = Coins.Total(bank);
            if (held == 0) continue;
            bank.Remove(d => d.HasTag("coin"), int.MaxValue);
            taken += held;
        }
        inv.Backpack.Remove(d => d.HasTag("coin"), int.MaxValue);
        Coins.Layout(inv, content, taken - need, overflow);
        return true;
    }

    /// <summary>What the player can spend at a shop: the purse plus the coins kept in personal storage.</summary>
    public static long Funds(PlayerInventory inv, IEnumerable<Inventory> savings) => Coins.Total(inv) + savings.Sum(Coins.Total);

    /// <summary>Adds coins worth <paramref name="copper"/> to the coin slots (converting as they fill), then the backpack; returns what did not fit.</summary>
    public static List<ItemStack> Give(PlayerInventory inv, ContentDatabase content, long copper)
    {
        var overflow = new List<ItemStack>();
        if (copper > 0) Coins.Layout(inv, content, Coins.Total(inv.Coins) + copper, overflow);
        return overflow;
    }

    /// <summary>
    /// Takes <paramref name="copper"/> worth of coins. All coins are gathered and the change is given back in the
    /// largest denominations; change that no longer fits is returned for dropping. Fails (changes nothing) when poor.
    /// </summary>
    public static bool TrySpend(Inventory inv, ContentDatabase content, long copper, out List<ItemStack> overflow)
    {
        overflow = new List<ItemStack>();
        if (copper < 0) throw new ArgumentOutOfRangeException(nameof(copper));
        long total = Coins.Total(inv);
        if (total < copper) return false;
        if (copper == 0) return true;
        inv.Remove(d => d.HasTag("coin"), int.MaxValue);
        overflow = Give(inv, content, total - copper);
        return true;
    }

    /// <summary>Adds coins worth <paramref name="copper"/>; returns what did not fit.</summary>
    public static List<ItemStack> Give(Inventory inv, ContentDatabase content, long copper)
    {
        var overflow = new List<ItemStack>();
        foreach (var stack in Coins.ToStacks(content, copper))
        {
            var rest = inv.Add(stack);
            if (!rest.IsEmpty) overflow.Add(rest);
        }
        return overflow;
    }
}
