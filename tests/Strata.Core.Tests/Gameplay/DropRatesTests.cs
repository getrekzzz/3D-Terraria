using Strata.Core.Content.Index;
using Strata.Core.Content.Loading;
using Strata.Core.Gameplay.Loot;
using Strata.Core.Tests.Support;

namespace Strata.Core.Tests.Gameplay;

public class DropRatesTests
{
    private static readonly Strata.Core.Content.ContentDatabase Db = ContentLoader.LoadFromTexts(new[] { ("test.json", """
        { "items": [ { "id": "a" }, { "id": "b" }, { "id": "c" }, { "id": "d" }, { "id": "e" } ],
          "enemies": [ { "id": "slime", "loot": "each", "value": 100 }, { "id": "rich", "loot": "pouch", "value": 100 } ],
          "lootTables": [
            { "id": "each", "entries": [ { "item": "a", "chance": 0.33 }, { "table": "weighted", "chance": 0.5 } ] },
            { "id": "weighted", "mode": "weighted", "rolls": 2, "entries": [ { "item": "b" }, { "item": "c" }, { "item": "d" }, { "item": "e" } ] },
            { "id": "pouch", "coins": [10, 50], "entries": [ { "item": "a", "chance": 1 } ] }
          ] }
        """) });

    [Fact]
    public void EachModeEntryKeepsItsChance()
    {
        var a = DropRates.For(Db, "each").First(d => d.Item.Id == "a");
        Assert.Equal(0.33f, a.Chance, 4);
    }

    [Fact]
    public void WeightedRollsCompound()
    {
        var b = DropRates.For(Db, "weighted").Single(d => d.Item.Id == "b");
        Assert.Equal(1f - 0.75f * 0.75f, b.Chance, 4);
    }

    [Fact]
    public void NestedTablesMultiply()
    {
        var b = DropRates.For(Db, "each").Single(d => d.Item.Id == "b");
        Assert.Equal(0.5f * (1f - 0.75f * 0.75f), b.Chance, 4);
        var source = Assert.Single(ItemSourceIndex.Build(Db).For("b"));
        Assert.Equal(b.Chance, source.Chance!.Value, 4);
    }

    [Fact]
    public void CoinRanges()
    {
        Assert.Equal((75L, 125L), DropRates.Coins(Db, Db.Enemies.Get("slime")));
        Assert.Equal((10L, 50L), DropRates.Coins(Db, Db.Enemies.Get("rich")));
    }

    [Fact]
    public void ShippedGelDropsFromSlimesWithAChance()
    {
        var gel = ItemSourceIndex.Build(TestPaths.Content).For("gel").First(s => s.Owner?.Id == "green_slime");
        Assert.NotNull(gel.Chance);
        Assert.InRange(gel.Chance!.Value, 0.01f, 1f);
        Assert.All(ItemSourceIndex.Build(TestPaths.Content).For("torch"), s => Assert.Equal(s.Kind is ItemSourceKind.Recipe or ItemSourceKind.Shop or ItemSourceKind.Tag, s.Chance == null));
    }
}
