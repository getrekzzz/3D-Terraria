using Strata.Core.Content;

namespace Strata.Core.Gameplay.Items;

/// <summary>Paying and receiving coins from an inventory, making change as needed.</summary>
public static class Wallet
{
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
