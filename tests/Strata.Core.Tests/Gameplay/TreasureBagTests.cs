using System.Numerics;
using Strata.Core.Conditions;
using Strata.Core.Determinism;
using Strata.Core.Gameplay.Interaction;
using Strata.Core.Gameplay.Loot;
using Strata.Core.Tests.Support;
using Strata.Core.World;

namespace Strata.Core.Tests.Gameplay;

/// <summary>Expert and Master bosses drop one treasure bag in place of their loose loot; every boss may drop a trophy.</summary>
public class TreasureBagTests
{
    public static readonly string[] Bosses =
        { "king_slime", "eye_of_cthulhu", "eater_of_worlds", "brain_of_cthulhu", "queen_bee", "skeletron", "deerclops", "wall_of_flesh" };

    // One Classic item each boss always drops loose (and its bag always holds).
    private static readonly Dictionary<string, string> Signature = new()
    {
        ["king_slime"] = "gel", ["eye_of_cthulhu"] = "lens", ["eater_of_worlds"] = "shadow_scale", ["brain_of_cthulhu"] = "tissue_sample",
        ["queen_bee"] = "bee_wax", ["skeletron"] = "lesser_healing_potion", ["deerclops"] = "lesser_healing_potion", ["wall_of_flesh"] = "pwnhammer",
    };

    private static readonly Dictionary<string, string> ExpertItem = new()
    {
        ["king_slime"] = "royal_gel", ["eye_of_cthulhu"] = "shield_of_cthulhu", ["eater_of_worlds"] = "worm_scarf",
        ["brain_of_cthulhu"] = "brain_of_confusion", ["queen_bee"] = "hive_pack",
    };

    private static TestGame Arena(Difficulty difficulty)
    {
        var g = TestGame.Create(difficulty: difficulty);
        g.World.Spawner.Enabled = false;
        return g;
    }

    /// <summary>Spawns and kills a boss; returns the ids of everything it dropped.</summary>
    private static List<string> Slay(TestGame g, string boss)
    {
        foreach (var d in g.World.Drops.Items.ToList()) g.World.Drops.Remove(d);
        var npc = g.World.Npcs.Spawn(g.Content.Enemies.Get(boss), g.Player.Position + new Vector3(0, 6, 14))!;
        Assert.NotNull(npc);
        g.World.Npcs.Kill(npc);
        var ids = g.World.Drops.Items.Select(d => d.Stack.Item!.Id).ToList();
        g.World.Npcs.Clear();
        return ids;
    }

    [Fact]
    public void ClassicBossesDropLooseLootAndNoBag()
    {
        using var g = Arena(Difficulty.Classic);
        foreach (var boss in Bosses)
        {
            var drops = Slay(g, boss);
            Assert.DoesNotContain(drops, id => id.EndsWith("_treasure_bag"));
            Assert.Contains(Signature[boss], drops);
            if (ExpertItem.TryGetValue(boss, out var expert)) Assert.DoesNotContain(expert, drops);
        }
    }

    [Fact]
    public void ExpertBossesDropOneBagAndNoLooseClassicItems()
    {
        foreach (var difficulty in new[] { Difficulty.Expert, Difficulty.Master })
        {
            using var g = Arena(difficulty);
            foreach (var boss in Bosses)
            {
                var drops = Slay(g, boss);
                Assert.Single(drops, id => id == $"{boss}_treasure_bag");
                Assert.DoesNotContain(drops, id => id.EndsWith("_treasure_bag") && id != $"{boss}_treasure_bag");
                foreach (var e in g.Content.LootTables.Get(boss + "_drops").Entries)
                    if (e.Item != null) Assert.DoesNotContain(e.Item, drops);
                if (ExpertItem.TryGetValue(boss, out var expert)) Assert.DoesNotContain(expert, drops);
            }
        }
    }

    [Fact]
    public void OpeningABagYieldsItsExpertItem()
    {
        using var g = Arena(Difficulty.Expert);
        foreach (var (boss, expert) in ExpertItem)
        {
            g.Hold($"{boss}_treasure_bag", 2, slot: 3);
            Assert.Equal(UseOutcome.Consumed, g.World.ItemUse.Use(g.Player, null, true));
            var inv = g.Player.State.Inventory.Backpack;
            Assert.Equal(1, inv.Count(g.Content.Items.Get(expert)));
            Assert.True(inv.Count(g.Content.Items.Get(Signature[boss])) > 0, $"{boss}'s bag held no {Signature[boss]}");
            Assert.Equal(1, inv[3].Count); // one bag used
            inv[3] = default;
            g.Ticks(30);
        }
    }

    [Fact]
    public void TrophiesDropAboutOneInTen()
    {
        using var g = Arena(Difficulty.Classic);
        var roller = new LootRoller(g.Content);
        var rng = new Pcg32(7);
        foreach (var boss in Bosses)
        {
            Assert.True(g.Content.TileObjects.TryGet($"{boss}_trophy", out var trophy));
            Assert.Equal(Strata.Core.Content.Defs.TileAnchor.Wall, trophy!.Anchor);
            int hits = 0;
            for (int i = 0; i < 2000; i++)
                if (roller.Roll(boss, ref rng, new ConditionContext()).Any(s => s.Item!.Id == $"{boss}_trophy")) hits++;
            Assert.InRange(hits / 2000f, 0.07f, 0.13f);
        }
    }

    [Fact]
    public void JourneyWorldsDropNoBags()
    {
        using var g = Arena(Difficulty.Journey);
        foreach (var boss in Bosses)
        {
            var drops = Slay(g, boss);
            Assert.DoesNotContain(drops, id => id.EndsWith("_treasure_bag"));
            if (ExpertItem.TryGetValue(boss, out var expert)) Assert.DoesNotContain(expert, drops);
        }
    }
}
