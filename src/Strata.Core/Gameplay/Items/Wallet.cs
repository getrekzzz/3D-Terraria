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
