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
        "glimmer_sprite", "prism_steed", "lustre_gel", "flux_stalker", "blight_drifter", "umbral_gel", "clot_gel",
        "ichor_spitter", "gore_drifter", "armored_bones", "dread_wraith", "gloom_bat",
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
    [InlineData("hallowed", WorldLayer.Surface, true, 15, new[] { "glimmer_sprite", "prism_steed" })]
    [InlineData("hallowed", WorldLayer.Cavern, true, 3, new[] { "lustre_gel", "flux_stalker", "armored_bones", "gloom_bat" })]
    [InlineData("umbral", WorldLayer.Surface, true, 15, new[] { "blight_drifter", "umbral_gel" })]
    [InlineData("sanguine", WorldLayer.Underground, true, 4, new[] { "clot_gel", "ichor_spitter", "gloom_bat" })]
    [InlineData("sanguine", WorldLayer.Surface, false, 15, new[] { "clot_gel", "gore_drifter", "dread_wraith" })]
    [InlineData("forest", WorldLayer.Surface, false, 15, new[] { "dread_wraith" })]
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
        Assert.Equal(new[] { "hellforge" }, Stations("cinder_bar"));
        Assert.Equal(new[] { "furnace" }, Stations("cerulite_bar"));
        Assert.Equal(new[] { "anvil" }, Stations("cerulite_pickaxe"));
        Assert.Equal(new[] { "hardmode_anvil" }, Stations("verdite_pickaxe"));
        Assert.Equal(new[] { "hardmode_forge" }, Stations("crimsite_bar"));
        Assert.Equal(new[] { "hardmode_anvil" }, Stations("stellite_broadsword"));

        // Each station object provides the stations the chain needs, and the better ones stand in for the old.
        Assert.Contains("hellforge", content.TileObjects.Get("cinder_forge").Stations);
        Assert.Contains("furnace", content.TileObjects.Get("cinder_forge").Stations);
        foreach (var anvil in new[] { "verdite_anvil", "orchidite_anvil" })
            Assert.Equal(new[] { "anvil", "hardmode_anvil" }, content.TileObjects.Get(anvil).Stations);
        foreach (var forge in new[] { "crimsite_forge", "stellite_forge" })
            Assert.Equal(new[] { "furnace", "hellforge", "hardmode_forge" }, content.TileObjects.Get(forge).Stations);

        // The chain is climbable: the hardmode anvil is forged from verdite at a plain anvil; the forge needs it.
        Assert.Equal(new[] { "anvil" }, Stations("verdite_anvil"));
        Assert.Contains(content.Recipes.Get("crimsite_forge").Inputs, i => i.Item == "cinder_forge");
        Assert.Equal(new[] { "hardmode_anvil" }, Stations("crimsite_forge"));
    }

    [Fact]
    public void IchorStripsACreaturesArmour()
    {
        using var g = TestGame.Create();
        g.World.Spawner.Enabled = false;
        var content = g.Content;
        var npc = g.World.Npcs.Spawn(content.Enemies.Get("armored_bones"), g.Player.Position + new Vector3(4, 1, 0))!;
        Assert.Equal(36, npc.Defense);
        g.World.Npcs.AddBuff(npc, content.Buffs.Get("ichor"), 300);
        Assert.Equal(21, npc.Defense);

        // The same hits hurt more through ichor (five each, so the damage spread cannot flip the result).
        var plain = g.World.Npcs.Spawn(content.Enemies.Get("armored_bones"), g.Player.Position + new Vector3(-4, 1, 0))!;
        int before = plain.Life, beforeIchor = npc.Life;
        for (int i = 0; i < 5; i++)
        {
            g.World.Npcs.Hit(plain, null, 60, DamageClass.Ranged, 0, 0f, g.Player.Center, melee: false);
            g.World.Npcs.Hit(npc, null, 60, DamageClass.Ranged, 0, 0f, g.Player.Center, melee: false);
        }
        Assert.True(beforeIchor - npc.Life > (before - plain.Life) * 1.08f, $"ichor {beforeIchor - npc.Life} vs plain {before - plain.Life}");
    }
}
