using System.Numerics;
using Strata.Core.Common;
using Strata.Core.Conditions;
using Strata.Core.Content;
using Strata.Core.Content.Loading;
using Strata.Core.Determinism;
using Strata.Core.Gameplay.Entities;
using Strata.Core.Gameplay.Items;
using Strata.Core.Gameplay.Loot;
using Strata.Core.Gameplay.Town;
using Strata.Core.Tests.Support;

namespace Strata.Core.Tests.Gameplay;

/// <summary>The Dungeon's locked-chest treasures, Golden Keys from kills and slimes, and town residents' death drops.</summary>
public class DungeonLootTests
{
    private static readonly ContentDatabase Db = TestPaths.Content;

    private static readonly string[] Treasures = { "muramasa", "cobalt_shield", "aqua_scepter", "handgun", "blue_moon", "magic_missile", "valor" };

    [Fact]
    public void EveryDungeonChestRollsOnePrimaryTreasure()
    {
        var loot = new LootRoller(Db);
        var rng = new Pcg32(11);
        var seen = new HashSet<string>();
        for (int i = 0; i < 300; i++)
        {
            var items = loot.Roll("dungeon_chest", ref rng).Select(s => s.Item!.Id).ToList();
            Assert.Single(items, Treasures.Contains);
            Assert.Contains(items, id => Db.Items.Get(id).HasTag("coin"));
            seen.UnionWith(items);
        }
        Assert.Superset(Treasures.ToHashSet(), seen); // every treasure turns up
        Assert.Contains("bone_welder", seen);
        Assert.DoesNotContain("shadow_key", seen);
        // Cabin chests no longer carry dungeon treasure, even inside the dungeon.
        var cabin = Db.LootTables.Get("cabin_chest_primary").Entries.Select(e => e.Item).ToHashSet();
        Assert.Empty(Treasures.Where(cabin.Contains));
    }

    [Fact]
    public void TheShadowKeyTableAlwaysHoldsTheKey()
    {
        var loot = new LootRoller(Db);
        var rng = new Pcg32(5);
        for (int i = 0; i < 20; i++)
        {
            var items = loot.Roll("dungeon_chest_shadow_key", ref rng).Select(s => s.Item!.Id).ToList();
            Assert.Contains("shadow_key", items);
            Assert.Single(items, Treasures.Contains);
        }
    }

    [Fact]
    public void GoldenKeysDropAboutOneInSixtyFiveInsideTheDungeonOnly()
    {
        var loot = new LootRoller(Db);
        int Keys(string biome, ulong seed)
        {
            var rng = new Pcg32(seed);
            var ctx = new ConditionContext { Biomes = new[] { biome } };
            int keys = 0;
            for (int i = 0; i < 65_000; i++)
                foreach (var s in loot.Roll(NpcSystem.GlobalKillTable, ref rng, ctx))
                    if (s.Item!.Id == "golden_key") keys += s.Count;
            return keys;
        }
        Assert.InRange(Keys("dungeon", 7), 850, 1150);
        Assert.Equal(0, Keys("forest", 7));
    }

    [Fact]
    public void DungeonSlimesAlwaysCarryAKey()
    {
        var loot = new LootRoller(Db);
        var rng = new Pcg32(3);
        for (int i = 0; i < 20; i++)
            Assert.Contains(loot.Roll(Db.Enemies.Get("dungeon_slime").Loot!, ref rng), s => s.Item!.Id == "golden_key");
    }

    [Fact]
    public void AngryBonesComeInFourLooksWithOneTable()
    {
        foreach (var id in new[] { "angry_bones", "angry_bones_big", "angry_bones_big_muscle", "angry_bones_big_helmet" })
        {
            var e = Db.Enemies.Get(id);
            Assert.Equal((80, 26, 8, "angry_bones"), (e.Life, e.Damage, e.Defense, e.Loot));
            Assert.Contains(Db.SpawnRules.All, r => r.Enemy == id);
        }
    }

    [Fact]
    public void ResidentsDropTheirDeathTableWhenAPlayerWearsTheItem()
    {
        // The shipped Clothier table names the Voodoo Doll; here an overlay table stands in with Silk.
        const string table = """{ "lootTables": [{ "id": "clothier_death", "entries": [{ "item": "silk", "conditions": [{ "type": "wearing", "item": "red_hat" }] }] }] }""";
        var content = ContentLoader.Load(new OverlaySource(TestPaths.ProjectRoot, "loot/zz_test_death.json", table));
        Assert.False(content.Report.HasErrors, content.Report.ToString());
        using var g = TestGame.Create(content: content);
        g.World.Spawner.Enabled = false;
        var town = g.World.Town;
        town.ArrivalsEnabled = false;
        int Silk() => g.World.Drops.Items.Where(d => d.Stack.Item!.Id == "silk").Sum(d => d.Stack.Count);

        var clothier = town.Summon(content.TownNpcs.Get("clothier"), g.Floor + new Int3(0, 1, 3));
        town.Slay(clothier);
        Assert.Equal(0, Silk()); // nobody wears the Red Hat

        g.Player.State.Inventory.Armor[0] = new ItemStack(content.Items.Get("red_hat"), 1);
        clothier.Life = clothier.Def.Life;
        clothier.RespawnTicks = 0;
        town.Slay(clothier);
        Assert.Equal(1, Silk());
        Assert.Equal("clothier_death", TownSystem.DeathTable("clothier"));
    }

    /// <summary>The shipped data plus one extra file listed in the manifest.</summary>
    private sealed class OverlaySource : IDataSource
    {
        private readonly FileSystemDataSource _disk;
        private readonly string _path, _text;

        public OverlaySource(string root, string file, string text) => (_disk, _path, _text) = (new FileSystemDataSource(root), "data/" + file, text);

        public bool Exists(string path) => path == _path || _disk.Exists(path);

        public string ReadText(string path)
        {
            if (path == _path) return _text;
            var text = _disk.ReadText(path);
            if (path != ContentLoader.ManifestPath) return text;
            int files = text.IndexOf("\"files\": [", StringComparison.Ordinal) + "\"files\": [".Length;
            return text.Insert(files, $"\"{_path["data/".Length..]}\", ");
        }
    }
}
