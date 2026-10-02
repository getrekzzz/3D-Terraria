using System.Numerics;
using Strata.Core.Conditions;
using Strata.Core.Determinism;
using Strata.Core.Gameplay.Interaction;
using Strata.Core.Gameplay.Loot;
using Strata.Core.Persistence;
using Strata.Core.Tests.Support;
using Strata.Core.World;

namespace Strata.Core.Tests.Gameplay;

/// <summary>P4-25: hardmode treasure bags and trophies, the Demon Heart and the Minecart Upgrade Kit pieces.</summary>
public class HardmodeLootTests
{
    // Loot table (= bag prefix) -> the Expert-only item its bag holds (null: none yet).
    private static readonly Dictionary<string, string?> Bags = new()
    {
        ["queen_slime"] = "volatile_gelatin", ["the_twins"] = "mechanical_wheel_piece", ["the_destroyer"] = "mechanical_battery_piece",
        ["skeletron_prime"] = "mechanical_wagon_piece", ["plantera"] = "spore_sac", ["golem"] = "shiny_stone",
        ["duke_fishron"] = "shrimpy_truffle", ["empress_of_light"] = null, ["moon_lord"] = null, ["wall_of_flesh"] = "demon_heart",
    };

    private static List<string> Roll(LootRoller roller, string table, int difficulty, ref Pcg32 rng) =>
        roller.Roll(table, ref rng, new ConditionContext { Difficulty = difficulty }).Select(s => s.Item!.Id).ToList();

    [Fact]
    public void EveryBossButTheCultistDropsABagInExpert()
    {
        using var g = TestGame.Create();
        var roller = new LootRoller(g.Content);
        var rng = new Pcg32(3);
        foreach (var (boss, expert) in Bags)
        {
            Assert.True(g.Content.Items.TryGet($"{boss}_treasure_bag", out _), boss);
            var classic = Roll(roller, boss, 0, ref rng);
            Assert.DoesNotContain(classic, id => id.EndsWith("_treasure_bag"));
            if (expert != null) Assert.DoesNotContain(expert, classic);
            var drops = Roll(roller, boss, 1, ref rng);
            Assert.Single(drops, id => id == $"{boss}_treasure_bag");
            if (expert != null) Assert.Contains(expert, Roll(roller, $"{boss}_bag", 1, ref rng));
        }
        Assert.DoesNotContain(Roll(roller, "lunatic_cultist", 1, ref rng), id => id.EndsWith("_treasure_bag"));
        Assert.False(g.Content.Items.TryGet("lunatic_cultist_treasure_bag", out _));
    }

    [Fact]
    public void EveryHardmodeBossCanDropItsTrophy()
    {
        using var g = TestGame.Create();
        var roller = new LootRoller(g.Content);
        var rng = new Pcg32(11);
        var trophies = new Dictionary<string, string[]>
        {
            ["queen_slime"] = new[] { "queen_slime_trophy" }, ["the_twins"] = new[] { "retinazer_trophy", "spazmatism_trophy" },
            ["the_destroyer"] = new[] { "destroyer_trophy" }, ["skeletron_prime"] = new[] { "skeletron_prime_trophy" },
            ["plantera"] = new[] { "plantera_trophy" }, ["golem"] = new[] { "golem_trophy" }, ["duke_fishron"] = new[] { "duke_fishron_trophy" },
            ["empress_of_light"] = new[] { "empress_of_light_trophy" }, ["lunatic_cultist"] = new[] { "ancient_cultist_trophy" },
            ["moon_lord"] = new[] { "moon_lord_trophy" },
        };
        foreach (var (boss, ids) in trophies)
            foreach (var trophy in ids)
            {
                Assert.True(g.Content.TileObjects.TryGet(trophy, out _), trophy);
                int hits = 0;
                for (int i = 0; i < 1000; i++)
                    if (Roll(roller, boss, 0, ref rng).Contains(trophy)) hits++;
                Assert.InRange(hits / 1000f, 0.06f, 0.14f);
            }
    }

    [Fact]
    public void TheTwinsDropOneBagWhenBothFall()
    {
        using var g = TestGame.Create(difficulty: Difficulty.Expert);
        g.World.Spawner.Enabled = false;
        var ret = g.World.Npcs.Spawn(g.Content.Enemies.Get("retinazer"), g.Player.Position + new Vector3(0, 8, 14))!;
        var spaz = g.World.Npcs.All.Single(n => n.Def.Id == "spazmatism");
        g.World.Npcs.Kill(ret);
        Assert.DoesNotContain(g.World.Drops.Items, d => d.Stack.Item!.Id == "the_twins_treasure_bag");
        g.World.Npcs.Kill(spaz);
        Assert.Single(g.World.Drops.Items, d => d.Stack.Item!.Id == "the_twins_treasure_bag");
    }

    [Fact]
    public void DemonHeartOpensASixthSlotOnlyInExpert()
    {
        using (var classic = TestGame.Create())
        {
            classic.Hold("demon_heart", 1, slot: 2);
            Assert.Equal(UseOutcome.Blocked, classic.World.ItemUse.Use(classic.Player, null, true));
            Assert.False(classic.Player.State.ExtraAccessorySlot);
        }
        using var g = TestGame.Create(difficulty: Difficulty.Expert);
        g.Hold("demon_heart", 2, slot: 2);
        Assert.Equal(UseOutcome.Consumed, g.World.ItemUse.Use(g.Player, null, true));
        Assert.True(g.Player.State.ExtraAccessorySlot);
        Assert.Equal(1, g.Player.State.Inventory.Backpack[2].Count);
        g.Ticks(40);
        Assert.Equal(UseOutcome.Blocked, g.World.ItemUse.Use(g.Player, null, true)); // once per character
    }

    [Fact]
    public void DemonHeartAndMinecartUpgradePersist()
    {
        using var g = TestGame.Create();
        g.Player.State.ExtraAccessorySlot = true;
        g.Player.State.MinecartUpgrade = true;
        var back = PlayerSave.Deserialize(PlayerSave.From(g.Player.State).Serialize()).ToState(g.Content);
        Assert.True(back.ExtraAccessorySlot);
        Assert.True(back.MinecartUpgrade);
        var old = PlayerSave.Deserialize("{\"format\":\"strata-player\",\"version\":1,\"name\":\"Old\"}").ToState(g.Content);
        Assert.False(old.ExtraAccessorySlot);
        Assert.False(old.MinecartUpgrade);
    }

    [Fact]
    public void ThePiecesMakeTheKitWhichUpgradesTheCartOnce()
    {
        using var g = TestGame.Create();
        var recipe = g.Content.Recipes.Get("minecart_upgrade_kit");
        Assert.Contains("tinkerers_workshop", recipe.Stations);
        Assert.Equal(3, recipe.Inputs.Length);
        g.Hold("minecart_upgrade_kit", 2, slot: 2);
        Assert.Equal(UseOutcome.Consumed, g.World.ItemUse.Use(g.Player, null, true));
        Assert.True(g.Player.State.MinecartUpgrade);
        g.Ticks(40);
        Assert.Equal(UseOutcome.Blocked, g.World.ItemUse.Use(g.Player, null, true));
    }
}
