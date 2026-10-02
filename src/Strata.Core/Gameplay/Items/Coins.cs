using Strata.Core.Content;

namespace Strata.Core.Gameplay.Items;

/// <summary>The four coin denominations, cheapest first.</summary>
public enum CoinTier
{
    Copper,
    Silver,
    Gold,
    Platinum,
}

/// <summary>A copper amount split into whole coins of each denomination.</summary>
public readonly record struct CoinParts(long Platinum, int Gold, int Silver, int Copper);

/// <summary>Converts copper values to coin stacks (100 copper = 1 silver, 100 silver = 1 gold, 100 gold = 1 platinum).</summary>
public static class Coins
{
    private static readonly string[] Ids = { "platinum_coin", "gold_coin", "silver_coin", "copper_coin" };
    private static readonly long[] Values = { 1_000_000, 10_000, 100, 1 };

    public static List<ItemStack> ToStacks(ContentDatabase content, long copper)
    {
        var result = new List<ItemStack>();
        for (int i = 0; i < Ids.Length && copper > 0; i++)
        {
            long n = copper / Values[i];
            copper -= n * Values[i];
            if (n > 0 && content.Items.TryGet(Ids[i], out var coin)) result.Add(new ItemStack(coin, (int)n));
        }
        return result;
    }

    /// <summary>Total copper value of all coin stacks in an inventory.</summary>
    public static long Total(Inventory inv)
    {
        long total = 0;
        foreach (var s in inv.Stacks)
            if (s.Item!.HasTag("coin")) total += (long)s.Item.Value * s.Count;
        return total;
    }

    /// <summary>The largest coin in the amount (prices are coloured by it); copper for zero or less.</summary>
    public static CoinTier TopTier(long copper) => copper switch
    {
        >= 1_000_000 => CoinTier.Platinum,
        >= 10_000 => CoinTier.Gold,
        >= 100 => CoinTier.Silver,
        _ => CoinTier.Copper,
    };

    /// <summary>The amount as platinum, gold, silver and copper coins (each below 100 except platinum).</summary>
    public static CoinParts Parts(long copper)
    {
        if (copper <= 0) return default;
        return new CoinParts(copper / 1_000_000, (int)(copper / 10_000 % 100), (int)(copper / 100 % 100), (int)(copper % 100));
    }

    public static string Format(long copper)
    {
        if (copper <= 0) return "0 copper";
        var parts = new List<string>();
        string[] names = { "platinum", "gold", "silver", "copper" };
        for (int i = 0; i < Values.Length; i++)
        {
            long n = copper / Values[i];
            copper -= n * Values[i];
            if (n > 0) parts.Add($"{n} {names[i]}");
        }
        return string.Join(" ", parts);
    }
}
