using Strata.Core.Gameplay.Items;
using Strata.Core.Gameplay.Player;
using Strata.Core.Persistence;
using Strata.Core.Session;
using Strata.Core.Tests.Support;
using Strata.Core.World;

namespace Strata.Core.Tests.Gameplay;

public class CharacterTests
{
    [Fact]
    public void ClassicDeathDropsHalfTheCoins()
    {
        using var g = TestGame.Create();
        var inv = g.Player.State.Inventory;
        inv.Backpack[20] = new ItemStack(g.Content.Items.Get("silver_coin"), 10); // 1000 copper
        g.Player.State.Vitals.DamageDirect(10_000);
        g.Ticks(2);
        Assert.Equal(500, Coins.Total(inv));
        Assert.Equal(500, g.World.Drops.Items.Where(d => d.Stack.Item!.HasTag("coin")).Sum(d => CopperOf(d.Stack)));
        Assert.Contains(inv.Backpack.Stacks, s => s.Item?.Id == "copper_pickaxe"); // items are kept
    }

    [Fact]
    public void MediumcoreDeathDropsEverything()
    {
        using var g = TestGame.Create();
        g.Player.State.Difficulty = CharacterDifficulty.Mediumcore;
        int carried = g.Player.State.Inventory.Backpack.Stacks.Count(s => !s.IsEmpty);
        g.Player.State.Vitals.DamageDirect(10_000);
        g.Ticks(2);
        Assert.All(g.Player.State.Inventory.Backpack.Stacks, s => Assert.True(s.IsEmpty));
        Assert.Equal(carried, g.World.Drops.Items.Count);
        g.Ticks(GameWorld.RespawnDelayTicks + 5);
        Assert.True(g.Player.Alive);
    }

    [Fact]
    public void MediumcoreDeathDropsVanityAndMiscToo()
    {
        using var g = TestGame.Create();
        var state = g.Player.State;
        var inv = state.Inventory;
        state.Difficulty = CharacterDifficulty.Mediumcore;
        inv.Coins[0] = new ItemStack(g.Content.Items.Get("gold_coin"), 3);
        inv.Ammo[0] = new ItemStack(g.Content.Items.Get("wooden_arrow"), 50);
        inv.Armor[0] = new ItemStack(g.Content.Items.Get("copper_helmet"), 1);
        inv.Accessories[0] = new ItemStack(g.Content.Items.Get("cobalt_shield"), 1);
        inv.VanityArmor[0] = new ItemStack(g.Content.Items.Get("wood_helmet"), 1);
        inv.VanityAccessories[1] = new ItemStack(g.Content.Items.Get("obsidian_skull"), 1);
        inv.Misc[(int)MiscSlot.Hook] = new ItemStack(g.Content.Items.Get("grappling_hook"), 1);
        inv.Trash[0] = new ItemStack(g.Content.Items.Get("dirt"), 7);
        int carried = inv.AllStorages.Sum(c => c.Stacks.Count());
        Assert.True(state.Defense > 0);
        state.Vitals.DamageDirect(10_000);
        g.Ticks(2);
        Assert.All(inv.AllStorages, c => Assert.Empty(c.Stacks));
        Assert.Equal(carried, g.World.Drops.Items.Count);
        Assert.Null(inv.EquippedHook);
        Assert.Equal(0, state.Defense); // the stats follow the dropped gear
        Assert.Equal(7, inv.Trash[0].Count); // the trash was already thrown away
    }

    [Fact]
    public void HardcoreDeathIsPermanent()
    {
        var dir = TestWorlds.TempDir();
        using var g = TestGame.Create(dir);
        g.Player.State.Difficulty = CharacterDifficulty.Hardcore;
        g.World.SaveAll();
        var path = GameWorld.PlayerPath(Path.Combine(dir, "players"), g.Player.State.Name);
        Assert.True(File.Exists(path));
        bool lost = false;
        g.World.PlayerLost += _ => lost = true;
        g.Player.State.Vitals.DamageDirect(10_000);
        g.Ticks(GameWorld.RespawnDelayTicks + 5);
        Assert.True(lost);
        Assert.False(g.Player.Alive); // never respawns
        g.World.SaveAll();
        Assert.False(File.Exists(path)); // and is not written back
    }

    [Fact]
    public void LookDifficultyAndPlayTimeSurviveASave()
    {
        var state = PlayerState.CreateNew("Ada", TestPaths.Content);
        state.Look = new Appearance { Body = "female", HairStyle = "ponytail", HairColor = "#c89050", ShirtColor = "#40a050" };
        state.Difficulty = CharacterDifficulty.Mediumcore;
        state.PlayTicks = 7200;
        var dir = TestWorlds.TempDir();
        GameWorld.WritePlayer(dir, state);
        var loaded = GameWorld.LoadOrCreatePlayer(dir, "Ada", TestPaths.Content);
        Assert.Equal("player_f_ponytail", loaded.Look.ModelId);
        Assert.Equal("#c89050", loaded.Look.HairColor);
        Assert.Equal(CharacterDifficulty.Mediumcore, loaded.Difficulty);
        Assert.Equal(7200, loaded.PlayTicks);

        var entry = Assert.Single(SaveCatalog.Characters(dir));
        Assert.Equal("Ada", entry.Name);
        Assert.Equal(CharacterDifficulty.Mediumcore, entry.Difficulty);
        Assert.Equal(100, entry.MaxLife);
        SaveCatalog.DeleteCharacter(entry.Path);
        Assert.Empty(SaveCatalog.Characters(dir));
    }

    [Fact]
    public void WorldsAreListedWithTheirSettings()
    {
        var root = TestWorlds.TempDir();
        var dir = SaveCatalog.NewWorldDirectory(root, "My World!");
        Assert.EndsWith("My_World_", dir);
        var tables = new Strata.Core.Meshing.RenderTables(TestPaths.Content, _ => 0);
        WorldSession.Create(TestPaths.Content, new WorldSettings { Name = "My World!", Seed = 99, Size = WorldSizePreset.Tiny, Difficulty = Difficulty.Expert, Evil = EvilKind.Crimson },
            dir, tables, new Strata.Core.Diagnostics.MemoryLog());
        Assert.NotEqual(dir, SaveCatalog.NewWorldDirectory(root, "My World!")); // taken: a new folder name
        var w = Assert.Single(SaveCatalog.Worlds(root));
        Assert.Equal("My World!", w.Name);
        Assert.Equal(Difficulty.Expert, w.Difficulty);
        Assert.Equal(EvilKind.Crimson, w.Evil);
        Assert.Equal(99, w.Seed);
        Assert.False(w.OldGenerator);

        // The same world as if an older generator had made it: listed as old, and loading it warns.
        var store = new WorldSaveStore(dir);
        var meta = store.LoadMeta();
        meta.GeneratorVersion = Strata.Core.WorldGen.GenVersion.Current - 1;
        store.SaveMeta(meta);
        Assert.True(Assert.Single(SaveCatalog.Worlds(root)).OldGenerator);
        var log = new Strata.Core.Diagnostics.MemoryLog();
        WorldSession.Load(TestPaths.Content, dir, tables, log);
        Assert.Contains(log.Entries, e => e.Level == Strata.Core.Diagnostics.LogLevel.Warning && e.Message.Contains("generator version", StringComparison.Ordinal));
        SaveCatalog.DeleteWorld(w.Directory);
        Assert.Empty(SaveCatalog.Worlds(root));
    }

    [Fact]
    public void SavesFromBeforeTheRenameStillLoad()
    {
        // A character saved with the old content ids: items and buffs resolve through data/config/aliases.json.
        var json = """
            { "format": "strata-player", "version": 1, "name": "Old", "life": 100, "mana": 20,
              "backpack": [ { "slot": 0, "item": "minor_restorative", "count": 3 }, { "slot": 1, "item": "loam", "count": 50 },
                            { "slot": 2, "item": "umbrite_pickaxe", "count": 1 } ],
              "buffs": [ { "id": "ironbark", "ticks": 600 } ] }
            """;
        var state = PlayerSave.Deserialize(json).ToState(TestPaths.Content);
        var inv = state.Inventory.Backpack;
        Assert.Equal("lesser_healing_potion", inv[0].Item!.Id);
        Assert.Equal("dirt", inv[1].Item!.Id);
        Assert.Equal("demonite_pickaxe", inv[2].Item!.Id);
        Assert.Equal("Nightmare Pickaxe", inv[2].Item!.DisplayName);
        Assert.Contains(state.Buffs.Active, b => b.Def.Id == "ironskin");
        Assert.Equal("king_slime", TestPaths.Content.Current("gel_sovereign"));
        Assert.Equal("stone", TestPaths.Content.Current("stone"));
    }

    [Fact]
    public void NewSlotsTrashAndFavoritesSurviveASave()
    {
        var content = TestPaths.Content;
        ItemStack S(string id, int count = 1) => new(content.Items.Get(id), count);
        var state = PlayerState.CreateNew("Slots", content);
        var inv = state.Inventory;
        inv.Backpack.SetFavorite(0, true);
        inv.Coins[3] = S("gold_coin", 12);
        inv.Ammo[1] = S("wooden_arrow", 250);
        inv.Ammo.SetFavorite(1, true);
        inv.Trash[0] = S("dirt", 40);
        inv.VanityArmor[0] = S("wood_helmet");
        inv.Accessories[6] = S("obsidian_skull"); // the Master slot
        inv.VanityAccessories[2] = S("cobalt_shield");
        inv.Dyes[4] = S("gel", 2);
        inv.Misc[(int)MiscSlot.Hook] = S("ivy_whip");
        inv.MiscDyes[(int)MiscSlot.Hook] = S("gel");
        inv.Service[0] = S("copper_shortsword");
        inv.HiddenAccessories = 0b101;

        var json = PlayerSave.From(state).Serialize();
        Assert.Equal(2, json.Split("\"favorite\": true").Length - 1);
        Assert.DoesNotContain("\"favorite\": false", json);
        var dir = TestWorlds.TempDir();
        GameWorld.WritePlayer(dir, state);
        var loaded = GameWorld.LoadOrCreatePlayer(dir, "Slots", content).Inventory;
        Assert.True(loaded.Backpack.IsFavorite(0));
        Assert.False(loaded.Backpack.IsFavorite(1));
        Assert.Equal(S("gold_coin", 12), loaded.Coins[3]);
        Assert.Equal(S("wooden_arrow", 250), loaded.Ammo[1]);
        Assert.True(loaded.Ammo.IsFavorite(1));
        Assert.Equal(S("dirt", 40), loaded.Trash[0]);
        Assert.Equal("wood_helmet", loaded.VanityArmor[0].Item!.Id);
        Assert.Equal("obsidian_skull", loaded.Accessories[6].Item!.Id);
        Assert.Equal("cobalt_shield", loaded.VanityAccessories[2].Item!.Id);
        Assert.Equal(S("gel", 2), loaded.Dyes[4]);
        Assert.Equal("ivy_whip", loaded.EquippedHook!.Id);
        Assert.Equal("gel", loaded.MiscDyes[(int)MiscSlot.Hook].Item!.Id);
        Assert.Equal("copper_shortsword", loaded.Service[0].Item!.Id);
        Assert.Equal(0b101, loaded.HiddenAccessories);
        Assert.True(loaded.IsAccessoryHidden(2));
    }

    [Fact]
    public void OldSavesLoadWithEmptyNewSlots()
    {
        // A v1 character from before the full slot model: no coin, ammo, trash, vanity, dye or misc lists, no favourites.
        var json = """
            { "format": "strata-player", "version": 1, "name": "Veteran", "life": 120, "mana": 20, "bonusLife": 20, "selectedHotbar": 2,
              "backpack": [ { "slot": 0, "item": "copper_pickaxe", "count": 1, "prefix": null }, { "slot": 49, "item": "torch", "count": 99 } ],
              "armor": [ { "slot": 0, "item": "wood_helmet", "count": 1 } ],
              "accessories": [ { "slot": 4, "item": "cobalt_shield", "count": 1, "prefix": "sturdy" } ],
              "spawnPoints": { "w": [1, 2, 3] } }
            """;
        var state = PlayerSave.Deserialize(json).ToState(TestPaths.Content);
        var inv = state.Inventory;
        Assert.Equal("copper_pickaxe", inv.Backpack[0].Item!.Id);
        Assert.Equal(99, inv.Backpack[49].Count);
        Assert.Equal("wood_helmet", inv.Armor[0].Item!.Id);
        Assert.Equal("sturdy", inv.Accessories[4].Prefix!.Id);
        Assert.Equal(2, inv.SelectedHotbar);
        Assert.Equal(120, state.Vitals.Life);
        Assert.True(state.Defense > 0);
        Assert.True(inv.Trash[0].IsEmpty);
        Assert.All(new[] { inv.Coins, inv.Ammo, inv.VanityArmor, inv.VanityAccessories, inv.Dyes, inv.Misc, inv.MiscDyes, inv.Service },
            c => Assert.Empty(c.Stacks));
        Assert.False(inv.Backpack.IsFavorite(0));
        Assert.Equal(0, inv.HiddenAccessories);
        Assert.DoesNotContain("favorite", PlayerSave.From(state).Serialize());
    }

    private static long CopperOf(ItemStack s) => s.Item!.Id switch
    {
        "platinum_coin" => 1_000_000L * s.Count,
        "gold_coin" => 10_000L * s.Count,
        "silver_coin" => 100L * s.Count,
        _ => s.Count,
    };
}
