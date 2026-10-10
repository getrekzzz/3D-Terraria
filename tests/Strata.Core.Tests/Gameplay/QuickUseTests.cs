using Strata.Core.Content.Defs;
using Strata.Core.Gameplay.Interaction;
using Strata.Core.Gameplay.Items;
using Strata.Core.Presentation;
using Strata.Core.Tests.Support;

namespace Strata.Core.Tests.Gameplay;

/// <summary>Quick heal, quick mana and quick buff (H, J, B), and the Life Fruit's 400 max life gate.</summary>
public class QuickUseTests
{
    [Fact]
    public void QuickHealPicksTheLeastOverhealAndKeepsTheSelection()
    {
        using var g = TestGame.Create();
        var inv = g.Player.State.Inventory;
        var vitals = g.Player.State.Vitals;
        g.Hold("copper_shortsword", slot: 2);
        Put(g, 12, "lesser_healing_potion", 3);
        Put(g, 20, "greater_healing_potion", 3);
        Put(g, 30, "healing_potion", 3);
        var used = new List<ItemDef>();
        var refused = new List<string>();
        g.World.ItemUse.QuickUsed += (_, item) => used.Add(item);
        g.World.ItemUse.Refused += (_, message) => refused.Add(message);

        Assert.Equal(UseOutcome.None, g.World.ItemUse.QuickHeal(g.Player)); // full life: nothing to drink
        vitals.DamageDirect(60);
        int serial = g.Player.UseSerial;
        Assert.Equal(UseOutcome.Consumed, g.World.ItemUse.QuickHeal(g.Player));
        // 60 missing: the Healing Potion (100) beats the Lesser (50, too little) and the Greater (150, more overheal).
        Assert.Equal(2, inv.Backpack[30].Count);
        Assert.Equal(3, inv.Backpack[12].Count);
        Assert.Equal(3, inv.Backpack[20].Count);
        Assert.Equal(vitals.MaxLife, vitals.Life);
        Assert.Equal(new[] { "healing_potion" }, used.Select(i => i.Id));
        // The held item and slot stay; the drink plays as its own use animation.
        Assert.Equal(2, inv.SelectedHotbar);
        Assert.Equal("copper_shortsword", inv.Held.Item!.Id);
        Assert.Equal(serial + 1, g.Player.UseSerial);
        Assert.Equal("healing_potion", g.Player.UseItem!.Id);

        // Potion Sickness: nothing is drunk, and it says why.
        Assert.True(g.Player.State.Buffs.Has("potion_sickness"));
        vitals.DamageDirect(30);
        Assert.Equal(UseOutcome.Blocked, g.World.ItemUse.QuickHeal(g.Player));
        Assert.Equal(2, inv.Backpack[30].Count);
        Assert.Equal("Potion Sickness", refused[^1]);

        // Sickness gone: 30 missing takes the Lesser; 300 missing (max 400), more than any heals, the Greater.
        g.Player.State.Buffs.Remove("potion_sickness");
        Assert.Equal(UseOutcome.Consumed, g.World.ItemUse.QuickHeal(g.Player));
        Assert.Equal(2, inv.Backpack[12].Count);
        g.Player.State.Buffs.Remove("potion_sickness");
        g.Player.State.BonusLife = 300;
        g.Player.State.Recompute();
        vitals.Life = 100;
        Assert.Equal(UseOutcome.Consumed, g.World.ItemUse.QuickHeal(g.Player));
        Assert.Equal(2, inv.Backpack[20].Count);
        Assert.Equal(250, vitals.Life);
    }

    [Fact]
    public void QuickManaPicksTheLeastOverfill()
    {
        using var g = TestGame.Create();
        var inv = g.Player.State.Inventory;
        var vitals = g.Player.State.Vitals;
        g.Player.State.BonusMana = 180;
        g.Player.State.Recompute();
        Put(g, 15, "greater_mana_potion", 2);
        Put(g, 16, "lesser_mana_potion", 2);
        Put(g, 17, "mana_potion", 2);
        vitals.Mana = vitals.MaxMana;
        Assert.Equal(UseOutcome.None, g.World.ItemUse.QuickMana(g.Player));
        vitals.Mana = vitals.MaxMana - 40;
        Assert.Equal(UseOutcome.Consumed, g.World.ItemUse.QuickMana(g.Player));
        Assert.Equal(1, inv.Backpack[16].Count); // 50 covers 40 with the least to spare
        Assert.Equal(vitals.MaxMana, vitals.Mana);
        // Mana Sickness never stops a mana potion.
        vitals.Mana = 0;
        Assert.Equal(UseOutcome.Consumed, g.World.ItemUse.QuickMana(g.Player));
        Assert.Equal(1, inv.Backpack[15].Count); // 200 missing: the Greater fills it exactly
        Assert.Equal(200, vitals.Mana);
    }

    [Fact]
    public void QuickBuffDrinksEachMissingBuffOnceAndEatsTheBestFood()
    {
        using var g = TestGame.Create();
        var inv = g.Player.State.Inventory;
        var buffs = g.Player.State.Buffs;
        g.Hold("copper_shortsword", slot: 1);
        Put(g, 10, "swiftness_potion", 3);
        Put(g, 11, "ironskin_potion", 2);
        Put(g, 12, "swiftness_potion", 4);
        Put(g, 13, "candy_corn", 2); // Well Fed for 5 minutes
        Put(g, 14, "cooked_fish", 2); // Well Fed for 20 minutes
        Put(g, 15, "lesser_healing_potion", 2);
        var used = new List<string>();
        g.World.ItemUse.QuickUsed += (_, item) => used.Add(item.Id);

        Assert.Equal(3, g.World.ItemUse.QuickBuff(g.Player));
        Assert.Equal(new[] { "cooked_fish", "swiftness_potion", "ironskin_potion" }, used);
        Assert.True(buffs.Has("swiftness") && buffs.Has("ironskin") && buffs.Has("well_fed"));
        Assert.Equal(2, inv.Backpack[10].Count);
        Assert.Equal(1, inv.Backpack[11].Count);
        Assert.Equal(4, inv.Backpack[12].Count); // already under Swiftness
        Assert.Equal(2, inv.Backpack[13].Count); // one food only
        Assert.Equal(1, inv.Backpack[14].Count);
        Assert.Equal(2, inv.Backpack[15].Count); // healing is not a buff
        Assert.Equal(1, inv.SelectedHotbar);

        // Everything is already on: nothing more is used.
        Assert.Equal(0, g.World.ItemUse.QuickBuff(g.Player));
        Assert.Equal(3, used.Count);

        // Food tiers conflict: with Well Fed on, a missing potion is drunk but no food is eaten.
        buffs.Remove("ironskin");
        Assert.Equal(1, g.World.ItemUse.QuickBuff(g.Player));
        Assert.Equal("ironskin_potion", used[^1]);
        Assert.Equal(2, inv.Backpack[13].Count);
        Assert.Equal(1, inv.Backpack[14].Count);
    }

    [Fact]
    public void LifeFruitNeedsFourHundredMaxLifeAndAMechanicalBoss()
    {
        using var g = TestGame.Create();
        var refused = new List<string>();
        g.World.ItemUse.Refused += (_, message) => refused.Add(message);
        g.Hold("life_fruit", 3);
        Assert.Equal(UseOutcome.Blocked, g.World.ItemUse.Use(g.Player, null, true));
        Assert.Equal("Requires 400 maximum life", refused[^1]);
        Assert.Equal(3, g.Player.State.Inventory.Held.Count);

        g.Player.State.BonusLife = 300;
        g.Player.State.Recompute();
        g.Player.ResetUse();
        Assert.Equal(UseOutcome.Blocked, g.World.ItemUse.Use(g.Player, null, true));
        Assert.Equal("Requires a mechanical boss to be defeated", refused[^1]);

        g.World.Progression.Set("defeated_mechanical_boss");
        g.Player.ResetUse();
        Assert.Equal(UseOutcome.Consumed, g.World.ItemUse.Use(g.Player, null, true));
        Assert.Equal(405, g.Player.State.Vitals.MaxLife);
        Assert.Equal(2, g.Player.State.Inventory.Held.Count);
    }

    [Fact]
    public void UsedPotionsFloatApartFromPickups()
    {
        var potion = new ItemDef { Id = "healing_potion", Name = "Healing Potion", MaxStack = 9999 };
        var feed = new PickupFeed();
        var picked = feed.Add(new ItemStack(potion, 5));
        var used = feed.AddUsed(potion);
        Assert.NotSame(picked, used);
        Assert.Equal("Used Healing Potion", used.Text);
        Assert.Same(used, feed.AddUsed(potion));
        Assert.Equal("Used Healing Potion (2)", used.Text);
        Assert.Same(picked, feed.Add(new ItemStack(potion, 1)));
        Assert.Equal("Healing Potion (6)", picked.Text);
    }

    private static void Put(TestGame g, int slot, string id, int count) =>
        g.Player.State.Inventory.Backpack[slot] = new ItemStack(g.Content.Items.Get(id), count);
}
