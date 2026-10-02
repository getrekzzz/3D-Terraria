using Strata.Core.Common;
using Strata.Core.Determinism;
using Strata.Core.Gameplay.Weather;
using Strata.Core.Voxels;

namespace Strata.Core.Gameplay.Fishing;

/// <summary>
/// Terraria's fishing math: power = (rod + bait + accessories and buffs) × weather × time of day × moon × events,
/// scaled down in small pools; bait consumption; and the catch rarity roll. Pure and allocation-free.
/// </summary>
public static class FishingPower
{
    /// <summary>The combined situational multiplier (weather is world-wide, as in Terraria).</summary>
    public static float Multiplier(FishingConfig c, Precipitation precipitation, float hour, int moonPhase, IReadOnlySet<string>? events)
    {
        float m = 1f;
        if (precipitation != Precipitation.None) m *= c.RainMultiplier;
        if (precipitation == Precipitation.Storm) m *= c.StormMultiplier;
        foreach (var h in c.Hours)
            if (h.From <= h.To ? hour >= h.From && hour < h.To : hour >= h.From || hour < h.To) m *= h.Multiplier;
        if (c.MoonMultipliers.Length > 0) m *= c.MoonMultipliers[((moonPhase % c.MoonMultipliers.Length) + c.MoonMultipliers.Length) % c.MoonMultipliers.Length];
        if (events != null)
            foreach (var (id, f) in c.EventMultipliers)
                if (events.Contains(id)) m *= f;
        return m;
    }

    /// <summary>
    /// Final fishing power: 0 when nothing can bite (no bait, or the pool is below <see cref="FishingConfig.MinPoolCells"/>).
    /// </summary>
    public static int Compute(FishingConfig c, int rodPower, int baitPower, float bonus, float multiplier, int poolCells)
    {
        if (baitPower <= 0 || poolCells < c.MinPoolCells) return 0;
        float power = (rodPower + baitPower + bonus) * multiplier;
        if (poolCells < c.FullPoolCells) power = power * poolCells / c.FullPoolCells;
        return Math.Max(1, (int)power);
    }

    /// <summary>Chance (0–1) that a catch uses up the bait: 1 / (1 + baitPower / divisor + saving).</summary>
    public static float BaitConsumeChance(FishingConfig c, int baitPower, float saving) =>
        1f / (1f + Math.Max(0, baitPower) / Math.Max(0.01f, c.BaitDivisor) + Math.Max(0f, saving));

    /// <summary>
    /// Rolls the catch rarity tier: 0 is a plain catch, i is <c>Rarities[i-1]</c>; each rarity succeeds with chance
    /// 1 / max(floor, odds / power) and the rarest success wins.
    /// </summary>
    public static int RollTier(FishingConfig c, int power, ref Pcg32 rng)
    {
        int tier = 0;
        if (power <= 0) return 0;
        for (int i = 0; i < c.Rarities.Length; i++)
        {
            var r = c.Rarities[i];
            float oneIn = Math.Max(r.Floor, r.Odds / power);
            if (rng.NextFloat() * oneIn < 1f) tier = i + 1;
        }
        return tier;
    }

    /// <summary>Name of a tier (<c>""</c> for the plain tier).</summary>
    public static string TierName(FishingConfig c, int tier) => tier <= 0 || tier > c.Rarities.Length ? "" : c.Rarities[tier - 1].Id;

    /// <summary>
    /// Counts the liquid cells of <paramref name="type"/> connected to <paramref name="start"/> (6-neighbour flood fill),
    /// stopping at <paramref name="cap"/>. The scratch collections are cleared and reused.
    /// </summary>
    public static int CountPool(VoxelWorld world, Int3 start, LiquidType type, int cap, Queue<Int3> open, HashSet<Int3> seen)
    {
        open.Clear();
        seen.Clear();
        if (LiquidCode.Type(world.GetLiquid(start.X, start.Y, start.Z)) != type) return 0;
        open.Enqueue(start);
        seen.Add(start);
        int count = 0;
        while (open.Count > 0 && count < cap)
        {
            var c = open.Dequeue();
            count++;
            Visit(world, c + new Int3(1, 0, 0), type, open, seen);
            Visit(world, c + new Int3(-1, 0, 0), type, open, seen);
            Visit(world, c + new Int3(0, 0, 1), type, open, seen);
            Visit(world, c + new Int3(0, 0, -1), type, open, seen);
            Visit(world, c + new Int3(0, -1, 0), type, open, seen);
            Visit(world, c + new Int3(0, 1, 0), type, open, seen);
        }
        return count;
    }

    private static void Visit(VoxelWorld world, Int3 c, LiquidType type, Queue<Int3> open, HashSet<Int3> seen)
    {
        if (c.Y < 0 || seen.Contains(c)) return;
        if (LiquidCode.Type(world.GetLiquid(c.X, c.Y, c.Z)) != type) return;
        seen.Add(c);
        open.Enqueue(c);
    }
}
