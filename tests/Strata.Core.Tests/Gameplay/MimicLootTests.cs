using System.Numerics;
using Strata.Core.Conditions;
using Strata.Core.Content.Defs;
using Strata.Core.Determinism;
using Strata.Core.Gameplay.Combat;
using Strata.Core.Gameplay.Entities;
using Strata.Core.Gameplay.Interaction;
using Strata.Core.Gameplay.Items;
using Strata.Core.Gameplay.Loot;
using Strata.Core.Gameplay.Stats;
using Strata.Core.Tests.Support;

namespace Strata.Core.Tests.Gameplay;

/// <summary>X-11: what the Mimics drop, and the Mimic accessories' effects (Titan Glove, Philosopher's Stone, Star Cloak).</summary>
public class MimicLootTests
{
    private static readonly string[] MimicSix = { "dual_hook", "magic_dagger", "titan_glove", "philosophers_stone", "cross_necklace", "star_cloak" };

    private static Dictionary<string, int> Tally(TestGame g, string table, int rolls)
    {
        var roller = new LootRoller(g.Content);
        var rng = new Pcg32(7);
        var tally = new Dictionary<string, int>();
        for (int i = 0; i < rolls; i++)
            foreach (var s in roller.Roll(table, ref rng, new ConditionContext()))
                if (s.Item!.Category != ItemCategory.Coin) tally[s.Item.Id] = tally.GetValueOrDefault(s.Item.Id) + s.Count;
        return tally;
    }

    [Fact]
    public void TheMimicDropsOneOfItsSixTreasuresEvenly()
    {
        using var g = TestGame.Create();
        var tally = Tally(g, "mimic", 6000);
        Assert.Equal(MimicSix.OrderBy(x => x), tally.Keys.OrderBy(x => x));
        foreach (var id in MimicSix) Assert.InRange(tally[id], 850, 1150); // ~1/6 each, exactly one per kill
        Assert.Equal(6000, tally.Values.Sum());
    }

    [Theory]
    [InlineData("corrupt_mimic", new[] { "worm_hook", "chain_guillotines", "clinger_staff", "putrid_scent" })]
    [InlineData("crimson_mimic", new[] { "tendon_hook", "fetid_baghnakhs", "life_drain", "flesh_knuckles" })]
    [InlineData("hallowed_mimic", new[] { "daedalus_stormbow", "flying_knife", "crystal_vile_shard", "illuminant_hook" })]
    public void BiomeMimicsDropTheirBiomesSetAndGreaterHealingPotions(string mimic, string[] pool)
    {
        using var g = TestGame.Create();
        Assert.Equal(mimic, g.Content.Enemies.Get(mimic).Loot);
        var tally = Tally(g, mimic, 400);
        foreach (var id in pool) Assert.InRange(tally[id], 60, 140);
        Assert.Equal(400, pool.Sum(id => tally[id]));
        Assert.InRange(tally["greater_healing_potion"], 400 * 5, 400 * 10);
    }

    [Fact]
    public void TheIceMimicDropsTheMimicsTreasureAndSometimesAToySled()
    {
        using var g = TestGame.Create();
        var tally = Tally(g, "ice_mimic", 600);
        Assert.Equal(600, MimicSix.Sum(id => tally.GetValueOrDefault(id)));
        Assert.InRange(tally["toy_sled"], 140, 260);
        Assert.Equal(EquipSlot.Pet, g.Content.Items.Get("toy_sled").EquipSlot);
    }

    private static TestGame Wearing(string accessory)
    {
        var g = TestGame.Create();
        g.World.Spawner.Enabled = false;
        g.Player.State.Inventory.Accessories[0] = new ItemStack(g.Content.Items.Get(accessory), 1);
        g.Player.State.Recompute();
        return g;
    }

    [Fact]
    public void TheTitanGloveDoublesMeleeKnockbackOnly()
    {
        using var g = Wearing("titan_glove");
        var stats = g.Player.State.Effects.Stats;
        Assert.Equal(1f, stats.Bonus(StatIds.MeleeKnockback), 3);
        var rng = new Pcg32(1);
        Assert.Equal(10f, DamageModel.Roll(20, DamageClass.Melee, -100, 5f, stats, 0, 0.5f, 0f, 0f, ref rng).Knockback, 3);
        Assert.Equal(5f, DamageModel.Roll(20, DamageClass.Ranged, -100, 5f, stats, 0, 0.5f, 0f, 0f, ref rng).Knockback, 3);
    }

    [Fact]
    public void ThePhilosophersStoneShortensPotionSicknessToFortyFiveSeconds()
    {
        foreach (var (accessory, ticks) in new[] { ("philosophers_stone", 2700), ("cobalt_shield", 3600) })
        {
            using var g = Wearing(accessory);
            g.Player.State.Vitals.DamageDirect(60);
            g.Hold("greater_healing_potion", 2);
            Assert.Equal(UseOutcome.Consumed, g.World.ItemUse.Use(g.Player, null, true));
            Assert.Equal(ticks, g.Player.State.Buffs.Active.Single(b => b.Def.Id == "potion_sickness").TicksLeft);
        }
    }

    [Fact]
    public void TheStarCloakCallsDownThreeStarsWhenHurt()
    {
        using var g = Wearing("star_cloak");
        int Stars() => g.World.Projectiles.All.Count(p => p.Def.Id == "star_cloak_star");
        g.World.HurtPlayerDirect(g.Player, 5, DamageKind.Periodic);
        Assert.Equal(0, Stars()); // debuffs ticking are not a hurt
        g.World.HurtPlayerDirect(g.Player, 12);
        Assert.Equal(3, Stars());
        foreach (var star in g.World.Projectiles.All.Where(p => p.Def.Id == "star_cloak_star"))
        {
            Assert.True(star.Position.Y > g.Player.Center.Y + 10f, "falls from high above");
            Assert.True(star.Velocity.Y < 0);
            Assert.Same(g.Player, star.Owner);
        }
    }

    [Fact]
    public void WithoutTheCloakNoStarsFall()
    {
        using var g = Wearing("cross_necklace");
        g.World.HurtPlayerDirect(g.Player, 12);
        Assert.DoesNotContain(g.World.Projectiles.All, p => p.Def.Id == "star_cloak_star");
    }
}
