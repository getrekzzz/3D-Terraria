using Strata.Core.Content.Defs;
using Strata.Core.Gameplay.Items;
using Strata.Core.Presentation;

namespace Strata.Core.Tests.Presentation;

public class PickupFeedTests
{
    private static readonly ItemDef Wood = new() { Id = "wood", Name = "Wood", MaxStack = 9999 };
    private static readonly ItemDef Ore = new() { Id = "copper_ore", Name = "Copper Ore", MaxStack = 9999 };
    private static readonly ItemDef Sword = new() { Id = "muramasa", Name = "Muramasa" };
    private static readonly PrefixDef Legendary = new() { Id = "legendary", Name = "Legendary" };
    private static readonly ItemDef Copper = new() { Id = "copper_coin", Name = "Copper Coin", Value = 1, Tags = new[] { "coin" } };
    private static readonly ItemDef Silver = new() { Id = "silver_coin", Name = "Silver Coin", Value = 100, Tags = new[] { "coin" } };
    private static readonly ItemDef Gold = new() { Id = "gold_coin", Name = "Gold Coin", Value = 10_000, Tags = new[] { "coin" } };

    [Fact]
    public void RepeatPickupsGrowOnePopup()
    {
        var feed = new PickupFeed();
        var first = feed.Add(new ItemStack(Wood, 30));
        feed.Tick(1.0);
        var again = feed.Add(new ItemStack(Wood, 7));
        Assert.Same(first, again);
        Assert.Equal(1, feed.ShownCount);
        Assert.Equal("Wood (37)", again.Text);
        Assert.Equal(0, again.Age); // started over
        Assert.Equal(2, again.Version);
    }

    [Fact]
    public void AKeptButFadedPopupComesBackWithItsCount()
    {
        var feed = new PickupFeed();
        var ore = feed.Add(new ItemStack(Ore, 1));
        feed.Tick(1.8); // past its life, still within the merge window
        Assert.Equal(0, feed.ShownCount);
        Assert.Same(ore, feed.Add(new ItemStack(Ore, 1)));
        Assert.Equal("Copper Ore (2)", ore.Text);
        feed.Tick(PickupFeed.MergeWindow + 0.1);
        var fresh = feed.Add(new ItemStack(Ore, 1));
        Assert.Equal("Copper Ore", fresh.Text); // a later pickup starts a new count
    }

    [Fact]
    public void ItemsAndPrefixesKeepTheirOwnPopups()
    {
        var feed = new PickupFeed();
        feed.Add(new ItemStack(Wood, 1));
        var plain = feed.Add(new ItemStack(Sword, 1));
        var legendary = feed.Add(new ItemStack(Sword, 1, Legendary));
        Assert.NotSame(plain, legendary);
        Assert.Equal(3, feed.ShownCount);
        Assert.Equal("Legendary Muramasa", legendary.Text);
    }

    [Fact]
    public void CoinsShareOnePopupSummingTheirValue()
    {
        var feed = new PickupFeed();
        feed.Add(new ItemStack(Silver, 23));
        feed.Add(new ItemStack(Gold, 1));
        var coins = feed.Add(new ItemStack(Copper, 5));
        Assert.Equal(1, feed.ShownCount);
        Assert.True(coins.IsCoins);
        Assert.Equal(12_305, coins.Amount);
        Assert.Equal("1 gold 23 silver 5 copper", coins.Text);
    }

    [Fact]
    public void ANewItemTakesTheOldestPopupWhenAllAreKept()
    {
        var feed = new PickupFeed();
        var items = new List<PickupFeed.Popup>();
        for (int i = 0; i < PickupFeed.Capacity; i++)
        {
            items.Add(feed.Add(new ItemStack(new ItemDef { Id = $"item_{i}", Name = $"Item {i}" }, 1)));
            feed.Tick(0.1);
        }
        var newest = feed.Add(new ItemStack(Wood, 1));
        Assert.Same(items[0], newest);
        Assert.Equal(PickupFeed.Capacity, feed.ShownCount);
    }

    [Fact]
    public void TheLatestPickupSitsAtTheBottomRow()
    {
        var feed = new PickupFeed();
        feed.Add(new ItemStack(Wood, 1));
        feed.Tick(0.3);
        feed.Add(new ItemStack(Ore, 1));
        feed.Tick(0.3);
        var popups = feed.Popups;
        int wood = Index(popups, Wood), ore = Index(popups, Ore);
        Assert.Equal(1, feed.Row(wood));
        Assert.Equal(0, feed.Row(ore));
        feed.Add(new ItemStack(Wood, 1)); // joining moves it back to the bottom
        Assert.Equal(0, feed.Row(wood));
        Assert.Equal(1, feed.Row(ore));
    }

    [Fact]
    public void PopupsPopRiseAndFade()
    {
        Assert.Equal(PickupFeed.PopScale, PickupFeed.Scale(0));
        Assert.Equal(1f, PickupFeed.Scale(PickupFeed.PopTime));
        Assert.Equal(0f, PickupFeed.Rise(0));
        Assert.Equal(PickupFeed.RisePixels, PickupFeed.Rise(PickupFeed.Life));
        Assert.True(PickupFeed.Rise(0.3) > PickupFeed.RisePixels * 0.3 / PickupFeed.Life); // eases out
        Assert.Equal(1f, PickupFeed.Alpha(PickupFeed.Life - PickupFeed.FadeTime));
        Assert.Equal(0.5f, PickupFeed.Alpha(PickupFeed.Life - PickupFeed.FadeTime / 2), 3);
        Assert.Equal(0f, PickupFeed.Alpha(PickupFeed.Life));
    }

    private static int Index(IReadOnlyList<PickupFeed.Popup> popups, ItemDef item)
    {
        for (int i = 0; i < popups.Count; i++)
            if (popups[i].Item == item && popups[i].Shown) return i;
        return -1;
    }
}
