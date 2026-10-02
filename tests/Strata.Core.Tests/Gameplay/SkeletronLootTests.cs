using Strata.Core.Common;
using Strata.Core.Conditions;
using Strata.Core.Determinism;
using Strata.Core.Gameplay.Interaction;
using Strata.Core.Gameplay.Items;
using Strata.Core.Gameplay.Loot;
using Strata.Core.Tests.Support;

namespace Strata.Core.Tests.Gameplay;

/// <summary>Skeletron's Terraria 1.4 drops, the Expert Bone Glove and the Clothier Voodoo Doll.</summary>
public class SkeletronLootTests
{
    private static TestGame Arena()
    {
        var g = TestGame.Create();
        g.World.Spawner.Enabled = false;
        g.World.Town.ArrivalsEnabled = false;
        return g;
    }

    [Fact]
    public void DropsFollowTerrariaOdds()
    {
        var loot = new LootRoller(TestPaths.Content);
        var rng = new Pcg32(11);
        var counts = new Dictionary<string, int>();
        const int rolls = 2800;
        for (int i = 0; i < rolls; i++)
            foreach (var s in loot.Roll("skeletron_drops", ref rng, new ConditionContext()))
                counts[s.Item!.Id] = counts.GetValueOrDefault(s.Item.Id) + 1;
        Assert.InRange(counts["skeletron_mask"] / (float)rolls, 1 / 7f - 0.03f, 1 / 7f + 0.03f);
        Assert.InRange(counts["skeletron_hand"] / (float)rolls, 0.125f - 0.03f, 0.125f + 0.03f);
        Assert.InRange(counts["book_of_skulls"] / (float)rolls, 0.125f - 0.03f, 0.125f + 0.03f);
        Assert.DoesNotContain("bone", counts.Keys); // not a Terraria drop
        var main = new Pcg32(3);
        var always = loot.Roll("skeletron", ref main, new ConditionContext());
        Assert.InRange(always.Where(s => s.Item!.Id == "lesser_healing_potion").Sum(s => s.Count), 5, 15);
        long copper = always.Where(st => st.Item!.HasTag("coin")).Sum(st => (long)st.Item!.Value * st.Count);
        Assert.InRange(copper, 45000, 55000); // about 5 gold
    }

    [Fact]
    public void ExpertBagHoldsTheBoneGlove()
    {
        var loot = new LootRoller(TestPaths.Content);
        var rng = new Pcg32(5);
        Assert.Contains(loot.Roll("skeletron_bag", ref rng), s => s.Item!.Id == "bone_glove");
        Assert.Contains(loot.Roll("skeletron", ref rng, new ConditionContext { Difficulty = 1 }), s => s.Item!.Id == "skeletron_treasure_bag");
    }

    [Fact]
    public void BoneGloveThrowsACrossboneOnAttackWithCooldown()
    {
        using var g = Arena();
        var inv = g.Player.State.Inventory;
        inv.Backpack[5] = new ItemStack(g.Content.Items.Get("bone_glove"), 1);
        inv.Equip(5);
        g.Player.State.Recompute();
        g.Hold("copper_broadsword");
        g.Player.Aim = System.Numerics.Vector3.UnitY; // thrown straight up, the bones stay aloft for the whole test
        int Bones() => g.World.Projectiles.All.Count(p => p.Def.Id == "bone_glove_bone");
        Assert.Equal(UseOutcome.Swung, g.World.ItemUse.Use(g.Player, null, true));
        Assert.Equal(1, Bones());
        g.Player.ResetUse();
        g.Ticks(20);
        Assert.Equal(UseOutcome.Swung, g.World.ItemUse.Use(g.Player, null, true));
        Assert.Equal(1, Bones()); // still cooling down (60 ticks)
        g.Ticks(45);
        g.Player.ResetUse();
        Assert.Equal(UseOutcome.Swung, g.World.ItemUse.Use(g.Player, null, true));
        Assert.Equal(2, Bones());
    }

    [Fact]
    public void TheClothierVoodooDollCursesTheClothierIntoSkeletronAtNight()
    {
        using var g = Arena();
        var skeletron = g.Content.Enemies.Get("skeletron");
        g.Hold("clothier_voodoo_doll", 2);
        g.World.Session.Clock.SetHour(12f);
        Assert.Equal(UseOutcome.Blocked, g.World.ItemUse.Use(g.Player, null, true)); // by day it refuses
        g.World.Session.Clock.SetHour(23f);
        g.Player.ResetUse();
        Assert.Equal(UseOutcome.Blocked, g.World.ItemUse.Use(g.Player, null, true)); // no Clothier to curse
        Assert.Equal(2, g.Player.State.Inventory.Held.Count);

        var at = g.World.Navigator.NearestStandable(g.Floor + new Int3(3, 1, 3), Strata.Core.Gameplay.Navigation.NavAgent.Humanoid, 3)!.Value;
        var clothier = g.World.Town.Summon(g.Content.TownNpcs.Get("clothier"), at);
        g.Player.ResetUse();
        Assert.Equal(UseOutcome.Consumed, g.World.ItemUse.Use(g.Player, null, true));
        var boss = Assert.Single(g.World.Npcs.All, n => n.Def == skeletron);
        Assert.True(boss.Position.Y > clothier.Position.Y + 5f);
        Assert.False(clothier.Alive);
        Assert.Equal(1, g.Player.State.Inventory.Held.Count);
        g.Player.ResetUse();
        Assert.Equal(UseOutcome.Blocked, g.World.ItemUse.Use(g.Player, null, true)); // Skeletron is already up
    }
}
