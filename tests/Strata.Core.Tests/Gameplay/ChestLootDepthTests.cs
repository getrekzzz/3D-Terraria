using System.Text.Json;
using Strata.Core.Conditions;
using Strata.Core.Content;
using Strata.Core.Determinism;
using Strata.Core.Gameplay.Loot;
using Strata.Core.Tests.Support;

namespace Strata.Core.Tests.Gameplay;

/// <summary>
/// Chest loot by chest type and by depth: every underground container grows richer (coins, stacks and rarer finds) the
/// deeper it lies, and at any one depth the chest types rank wooden-tier cave chests below Gold Chests below Obsidian Chests.
/// </summary>
public class ChestLootDepthTests
{
    private static readonly ContentDatabase Db = TestPaths.Content;
    private static readonly LootRoller Loot = new(Db);

    /// <summary>Average copper value (coins plus each item's value) of a table's roll at a world depth.</summary>
    private static double AverageValue(string table, float depth, int rolls = 4000)
    {
        var ctx = new ConditionContext { WorldDepth = depth };
        var rng = new Pcg32(77);
        double total = 0;
        for (int i = 0; i < rolls; i++)
            foreach (var s in Loot.Roll(table, ref rng, ctx)) total += (double)s.Item!.Value * s.Count;
        return total / rolls;
    }

    [Theory]
    [InlineData("cave_chest", 0.05f, 0.4f)]
    [InlineData("cabin_chest", 0.1f, 0.7f)]
    [InlineData("gold_chest", 0.4f, 0.75f)]
    [InlineData("obsidian_chest", 0.5f, 1f)]
    [InlineData("pot", 0.1f, 0.8f)]
    public void ContainersGrowRicherDeeper(string table, float shallow, float deep)
    {
        double a = AverageValue(table, shallow), b = AverageValue(table, deep);
        Assert.True(b > a * 1.1, $"{table}: {a:F0} copper at depth {shallow}, {b:F0} at {deep}");
    }

    [Theory]
    [InlineData(0.3f)]
    [InlineData(0.6f)]
    [InlineData(0.85f)]
    public void ChestTypesRankAtEveryDepth(float depth)
    {
        double surface = AverageValue("surface_chest", depth), cave = AverageValue("cave_chest", depth);
        double gold = AverageValue("gold_chest", depth), obsidian = AverageValue("obsidian_chest", depth);
        Assert.True(surface < cave && cave < gold && gold < obsidian,
            $"depth {depth}: surface {surface:F0}, cave {cave:F0}, gold {gold:F0}, obsidian {obsidian:F0}");
    }

    [Fact]
    public void GoldChestsFindDeepTreasureOnlyDeepDown()
    {
        // The deep treasures a Gold Chest has no other way to give (it holds a Fairy Bell now and then at any depth).
        var elsewhere = Db.LootTables.Get("gold_chest").Entries.Concat(Db.LootTables.Get("cabin_chest_primary").Entries).Select(e => e.Item);
        var deepItems = Db.LootTables.Get("deep_chest_primary").Entries.Select(e => e.Item!).Except(elsewhere).ToHashSet();
        double Share(float depth)
        {
            var ctx = new ConditionContext { WorldDepth = depth };
            var rng = new Pcg32(5);
            int hits = 0;
            for (int i = 0; i < 3000; i++)
                if (Loot.Roll("gold_chest", ref rng, ctx).Any(s => deepItems.Contains(s.Item!.Id))) hits++;
            return hits / 3000.0;
        }
        Assert.Equal(0, Share(0.45f));
        double mid = Share(0.6f), deep = Share(0.75f);
        Assert.InRange(mid, 0.1, 0.3);
        Assert.True(deep > mid, $"deep treasure in {mid:P0} of Gold Chests at 0.6, {deep:P0} at 0.75");
    }

    [Fact]
    public void ObsidianChestsAlwaysHoldDeepTreasureAndSingleItemsStaySingle()
    {
        var deepItems = Db.LootTables.Get("deep_chest_primary").Entries.Select(e => e.Item!).ToHashSet();
        var ctx = new ConditionContext { WorldDepth = 1f };
        var rng = new Pcg32(9);
        for (int i = 0; i < 300; i++)
        {
            var items = Loot.Roll("obsidian_chest", ref rng, ctx);
            Assert.Contains(items, s => deepItems.Contains(s.Item!.Id));
            // Depth multiplies stacks of potions, bars and ammunition, never an accessory or weapon.
            Assert.All(items.Where(s => s.Item!.MaxStack <= 1), s => Assert.Equal(1, s.Count));
        }
    }

    [Fact]
    public void WorldDepthConditionReadsTheContext()
    {
        var spec = new TypedSpec("worldDepth", JsonDocument.Parse("""{ "type": "worldDepth", "min": 0.5, "max": 0.8 }""").RootElement.Clone());
        var condition = ConditionCompiler.Compile(spec);
        Assert.False(condition.Evaluate(new ConditionContext { WorldDepth = 0.3f }));
        Assert.True(condition.Evaluate(new ConditionContext { WorldDepth = 0.6f }));
        Assert.False(condition.Evaluate(new ConditionContext { WorldDepth = 0.9f }));
        Assert.Equal("between 50% and 80% of the way to the underworld", ConditionDescriber.Describe(spec, Db));
    }

    [Fact]
    public void DropRatesCountTheDepthBonus()
    {
        var table = Db.LootTables.Get("cave_chest");
        var primary = table.Entries.First(e => e.Table == "cabin_chest_primary");
        Assert.Equal(0.5f, DropRates.EntryChance(table, primary), 3);
        Assert.Equal(0.66f, DropRates.EntryChance(table, primary, 0.4f), 3);
    }
}
