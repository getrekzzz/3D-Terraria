using Strata.Core.Content;
using Strata.Core.Content.Defs;

namespace Strata.Core.Gameplay.Items;

/// <summary>
/// The coin slots as the reference game keeps them: 100 of a coin turn into 1 of the next on their own, so each
/// denomination sits in one slot with fewer than 100 coins (platinum stacks up to its limit).
/// </summary>
public static partial class Coins
{
    /// <summary>
    /// Lays the coin slots out again as the fewest coins (250 copper become 2 silver and 50 copper). Coins beyond the
    /// four slots go to the backpack. Changes nothing and returns false when the backpack cannot take them either, or
    /// when the content lacks a coin.
    /// </summary>
    public static bool Normalize(PlayerInventory inv, ContentDatabase content) => Layout(inv, content, Total(inv.Coins), null);

    /// <summary>
    /// Replaces the coins in the coin slots with <paramref name="copper"/> worth of the fewest coins. Each denomination
    /// stays in the first slot that already held it (keeping its favourite mark) and new ones take the first free slot;
    /// other items in the coin slots stay put. What the coin slots cannot hold goes to the backpack, and what the
    /// backpack cannot hold goes to <paramref name="leftover"/>; with no list the call changes nothing and returns
    /// false instead.
    /// </summary>
    internal static bool Layout(PlayerInventory inv, ContentDatabase content, long copper, List<ItemStack>? leftover)
    {
        var slots = inv.Coins;
        int size = slots.Size;
        var target = new ItemStack[size];
        var free = new bool[size];
        Span<int> home = stackalloc int[Ids.Length];
        home.Fill(-1);
        for (int i = 0; i < size; i++)
        {
            var s = slots[i];
            target[i] = s;
            if (s.IsEmpty) free[i] = true;
            else if (IsCoin(s))
            {
                free[i] = true;
                target[i] = ItemStack.Empty;
                int tier = Array.IndexOf(Ids, s.Item!.Id);
                if (tier >= 0 && home[tier] < 0) home[tier] = i;
            }
        }

        var coins = new ItemDef?[Ids.Length];
        Span<long> amount = stackalloc long[Ids.Length];
        long rest = Math.Max(0, copper);
        for (int t = 0; t < Ids.Length; t++)
        {
            amount[t] = rest / Values[t];
            rest -= amount[t] * Values[t];
            if (amount[t] > 0 && !content.Items.TryGet(Ids[t], out coins[t])) return false;
        }

        // Denominations already in a slot fill that slot first, so the free slots are known before new ones take them.
        for (int t = 0; t < Ids.Length; t++)
        {
            if (amount[t] == 0 || home[t] < 0) continue;
            int n = (int)Math.Min(amount[t], coins[t]!.MaxStack);
            target[home[t]] = new ItemStack(coins[t]!, n);
            free[home[t]] = false;
            amount[t] -= n;
        }
        List<ItemStack>? overflow = null;
        for (int t = 0; t < Ids.Length; t++)
            while (amount[t] > 0)
            {
                int n = (int)Math.Min(amount[t], coins[t]!.MaxStack);
                amount[t] -= n;
                int slot = Array.IndexOf(free, true);
                if (slot < 0)
                {
                    (overflow ??= new List<ItemStack>()).Add(new ItemStack(coins[t]!, n));
                    continue;
                }
                target[slot] = new ItemStack(coins[t]!, n);
                free[slot] = false;
            }

        if (overflow != null && leftover == null)
        {
            var trial = new Inventory(inv.Backpack.ToArray());
            foreach (var s in overflow)
                if (!trial.Add(s).IsEmpty) return false;
        }
        for (int i = 0; i < size; i++)
            if (!slots[i].Equals(target[i])) slots[i] = target[i];
        if (overflow != null)
            foreach (var s in overflow)
            {
                var r = inv.Backpack.Add(s);
                if (!r.IsEmpty) leftover?.Add(r);
            }
        return true;
    }
}
