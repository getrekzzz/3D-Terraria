using System.Numerics;
using Strata.Core.Conditions;
using Strata.Core.Content.Defs;
using Strata.Core.Tests.Support;
using Strata.Core.World;

namespace Strata.Core.Tests.Gameplay;

public class HardmodeLifeTests
{
    private static readonly string[] Roster =
    {
        "pixie", "unicorn", "illuminant_slime", "chaos_elemental", "corruptor", "corrupt_slime", "crimslime",
        "ichor_sticker", "floaty_gross", "armored_skeleton", "wraith", "giant_bat",
    };

    private static HashSet<string> Eligible(ConditionContext ctx) =>
        TestPaths.Content.SpawnRules.All.Where(r => ConditionCompiler.CompileAll(r.Conditions).Evaluate(ctx)).Select(r => r.Enemy).ToHashSet();

    private static ConditionContext Where(string biome, WorldLayer layer, bool day, bool hardmode, int light = 15) => new()
    {
        Biomes = new[] { biome },
        Layer = layer,
        IsDay = day,
        Light = light,
        Flags = hardmode ? new HashSet<string> { "hardmode" } : new HashSet<string>(),
    };

    [Theory]
    [InlineData("hallow", WorldLayer.Surface, true, 15, new[] { "pixie", "unicorn" })]
    [InlineData("hallow", WorldLayer.Cavern, true, 3, new[] { "illuminant_slime", "chaos_elemental", "armored_skeleton", "giant_bat" })]
    [InlineData("corruption", WorldLayer.Surface, true, 15, new[] { "corruptor", "corrupt_slime" })]
    [InlineData("crimson", WorldLayer.Underground, true, 4, new[] { "crimslime", "ichor_sticker", "giant_bat" })]
    [InlineData("crimson", WorldLayer.Surface, false, 15, new[] { "crimslime", "floaty_gross", "wraith" })]
    [InlineData("forest", WorldLayer.Surface, false, 15, new[] { "wraith" })]
    public void HardmodeCreaturesAppearOnlyAfterTheMawwallFalls(string biome, WorldLayer layer, bool day, int light, string[] expected)
    {
        var before = Eligible(Where(biome, layer, day, hardmode: false, light));
        var after = Eligible(Where(biome, layer, day, hardmode: true, light));
        Assert.Empty(before.Intersect(Roster));
        Assert.Equal(expected.OrderBy(e => e), after.Intersect(Roster).OrderBy(e => e));
    }

    [Fact]
    public void EveryHardmodeCreatureHasASpawnRuleLootAndACover()
    {
        var content = TestPaths.Content;
        foreach (var id in Roster)
        {
            var def = content.Enemies.Get(id);
            Assert.Contains(content.SpawnRules.All, r => r.Enemy == id);
            Assert.NotNull(def.Loot);
            Assert.True(content.LootTables.TryGet(def.Loot!, out _), $"{id} has no loot table");
            Assert.NotEmpty(def.Covers);
        }
    }

    [Fact]
    public void HardmodeMetalsNeedTheirForgesAndAnvils()
    {
        var content = TestPaths.Content;
        string[] Stations(string recipe) => content.Recipes.Get(recipe).Stations;
        Assert.Equal(new[] { "hellforge" }, Stations("hellstone_bar"));
        Assert.Equal(new[] { "furnace" }, Stations("cobalt_bar"));
        Assert.Equal(new[] { "anvil" }, Stations("cobalt_pickaxe"));
        Assert.Equal(new[] { "hardmode_anvil" }, Stations("mythril_pickaxe"));
        Assert.Equal(new[] { "hardmode_forge" }, Stations("adamantite_bar"));
        Assert.Equal(new[] { "hardmode_anvil" }, Stations("titanium_broadsword"));

        // Each station object provides the stations the chain needs, and the better ones stand in for the old.
        Assert.Contains("hellforge", content.TileObjects.Get("hellforge").Stations);
        Assert.Contains("furnace", content.TileObjects.Get("hellforge").Stations);
        foreach (var anvil in new[] { "mythril_anvil", "orichalcum_anvil" })
            Assert.Equal(new[] { "anvil", "hardmode_anvil" }, content.TileObjects.Get(anvil).Stations);
        foreach (var forge in new[] { "adamantite_forge", "titanium_forge" })
            Assert.Equal(new[] { "furnace", "hellforge", "hardmode_forge" }, content.TileObjects.Get(forge).Stations);

        // The chain is climbable: the hardmode anvil is forged from verdite at a plain anvil; the forge needs it.
        Assert.Equal(new[] { "anvil" }, Stations("mythril_anvil"));
        Assert.Contains(content.Recipes.Get("adamantite_forge").Inputs, i => i.Item == "hellforge");
        Assert.Equal(new[] { "hardmode_anvil" }, Stations("adamantite_forge"));
    }

    [Fact]
    public void IchorStripsACreaturesArmour()
    {
        using var g = TestGame.Create();
        g.World.Spawner.Enabled = false;
        var content = g.Content;
        var npc = g.World.Npcs.Spawn(content.Enemies.Get("armored_skeleton"), g.Player.Position + new Vector3(4, 1, 0))!;
        Assert.Equal(36, npc.Defense);
        g.World.Npcs.AddBuff(npc, content.Buffs.Get("ichor"), 300);
        Assert.Equal(21, npc.Defense);

        // The same hits hurt more through ichor (five each, so the damage spread cannot flip the result).
        var plain = g.World.Npcs.Spawn(content.Enemies.Get("armored_skeleton"), g.Player.Position + new Vector3(-4, 1, 0))!;
        int before = plain.Life, beforeIchor = npc.Life;
        for (int i = 0; i < 5; i++)
        {
            g.World.Npcs.Hit(plain, null, 60, DamageClass.Ranged, 0, 0f, g.Player.Center, melee: false);
            g.World.Npcs.Hit(npc, null, 60, DamageClass.Ranged, 0, 0f, g.Player.Center, melee: false);
        }
        Assert.True(beforeIchor - npc.Life > (before - plain.Life) * 1.08f, $"ichor {beforeIchor - npc.Life} vs plain {before - plain.Life}");
    }
}
