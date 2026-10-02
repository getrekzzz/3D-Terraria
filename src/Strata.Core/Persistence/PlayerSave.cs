using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Strata.Core.Content;
using Strata.Core.Gameplay.Items;
using Strata.Core.Gameplay.Player;
using Strata.Core.Persistence.Migrations;

namespace Strata.Core.Persistence;

/// <summary>Versioned JSON save of a player character (content ids as strings; unknown ids become placeholders).</summary>
public sealed class PlayerSave
{
    public const string FormatId = "strata-player";
    public const int CurrentVersion = 1;

    public string Format { get; set; } = FormatId;
    public int Version { get; set; } = CurrentVersion;
    public string Name { get; set; } = "";
    public int Life { get; set; }
    public int Mana { get; set; }
    public int BonusLife { get; set; }
    public int BonusMana { get; set; }
    public int Deaths { get; set; }
    public int SelectedHotbar { get; set; }
    public List<SlotModel> Backpack { get; set; } = new();
    public List<SlotModel> Armor { get; set; } = new();
    public List<SlotModel> Accessories { get; set; } = new();
    public List<BuffModel> Buffs { get; set; } = new();
    public Dictionary<string, int[]> SpawnPoints { get; set; } = new();

    // Added without a version bump: older saves load with the defaults.
    public Appearance Look { get; set; } = new();
    public CharacterDifficulty Difficulty { get; set; }
    public long PlayTicks { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime LastPlayedUtc { get; set; }
    public int MaxLife { get; set; }
    public int MaxMana { get; set; }
    public bool ExtraAccessorySlot { get; set; }
    public bool MinecartUpgrade { get; set; }

    // The full slot model (also additive): coin, ammo and trash slots, vanity, dyes, the misc row, the service slot and
    // which accessories are hidden.
    public List<SlotModel> Coins { get; set; } = new();
    public List<SlotModel> Ammo { get; set; } = new();
    public List<SlotModel> Trash { get; set; } = new();
    public List<SlotModel> VanityArmor { get; set; } = new();
    public List<SlotModel> VanityAccessories { get; set; } = new();
    public List<SlotModel> Dyes { get; set; } = new();
    public List<SlotModel> Misc { get; set; } = new();
    public List<SlotModel> MiscDyes { get; set; } = new();
    public List<SlotModel> Service { get; set; } = new();
    public int HiddenAccessories { get; set; }

    /// <summary>The character's journal (additive: older saves load with an empty one).</summary>
    public JournalModel? Journal { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }

    /// <summary>One filled slot; <paramref name="Favorite"/> is written only when set.</summary>
    public sealed record SlotModel(int Slot, string Item, int Count, string? Prefix,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Favorite = false);

    public sealed record BuffModel(string Id, int Ticks);

    public static PlayerSave From(PlayerState p) => new()
    {
        Name = p.Name,
        Life = p.Vitals.Life,
        Mana = p.Vitals.Mana,
        BonusLife = p.BonusLife,
        BonusMana = p.BonusMana,
        Deaths = p.Deaths,
        SelectedHotbar = p.Inventory.SelectedHotbar,
        Backpack = Slots(p.Inventory.Backpack),
        Armor = Slots(p.Inventory.Armor),
        Accessories = Slots(p.Inventory.Accessories),
        Coins = Slots(p.Inventory.Coins),
        Ammo = Slots(p.Inventory.Ammo),
        Trash = Slots(p.Inventory.Trash),
        VanityArmor = Slots(p.Inventory.VanityArmor),
        VanityAccessories = Slots(p.Inventory.VanityAccessories),
        Dyes = Slots(p.Inventory.Dyes),
        Misc = Slots(p.Inventory.Misc),
        MiscDyes = Slots(p.Inventory.MiscDyes),
        Service = Slots(p.Inventory.Service),
        HiddenAccessories = p.Inventory.HiddenAccessories,
        Buffs = p.Buffs.Active.Where(b => !b.Def.Debuff).Select(b => new BuffModel(b.Def.Id, b.TicksLeft)).ToList(),
        SpawnPoints = new Dictionary<string, int[]>(p.SpawnPoints),
        Look = p.Look.Clone(),
        Difficulty = p.Difficulty,
        PlayTicks = p.PlayTicks,
        CreatedUtc = p.CreatedUtc,
        LastPlayedUtc = p.LastPlayedUtc,
        MaxLife = p.Vitals.MaxLife,
        MaxMana = p.Vitals.MaxMana,
        ExtraAccessorySlot = p.ExtraAccessorySlot,
        MinecartUpgrade = p.MinecartUpgrade,
        Journal = JournalModel.From(p.Journal),
    };

    public PlayerState ToState(ContentDatabase content)
    {
        var p = new PlayerState(Name)
        {
            BonusLife = BonusLife, BonusMana = BonusMana, Deaths = Deaths, Look = Look ?? new Appearance(), Difficulty = Difficulty,
            PlayTicks = PlayTicks, CreatedUtc = CreatedUtc, LastPlayedUtc = LastPlayedUtc,
            ExtraAccessorySlot = ExtraAccessorySlot, MinecartUpgrade = MinecartUpgrade,
        };
        p.Inventory.Content = content;
        Fill(p.Inventory.Backpack, Backpack, content);
        Fill(p.Inventory.Armor, Armor, content);
        Fill(p.Inventory.Accessories, Accessories, content);
        Fill(p.Inventory.Coins, Coins, content);
        Fill(p.Inventory.Ammo, Ammo, content);
        Fill(p.Inventory.Trash, Trash, content);
        Fill(p.Inventory.VanityArmor, VanityArmor, content);
        Fill(p.Inventory.VanityAccessories, VanityAccessories, content);
        Fill(p.Inventory.Dyes, Dyes, content);
        Fill(p.Inventory.Misc, Misc, content);
        Fill(p.Inventory.MiscDyes, MiscDyes, content);
        Fill(p.Inventory.Service, Service, content);
        p.Inventory.HiddenAccessories = HiddenAccessories;
        foreach (var b in Buffs)
            if (content.Buffs.TryGet(content.Current(b.Id), out var def)) p.Buffs.Add(def, b.Ticks);
        foreach (var kv in SpawnPoints) p.SpawnPoints[kv.Key] = kv.Value;
        Journal?.Fill(p.Journal);
        p.Inventory.Select(SelectedHotbar);
        p.Recompute();
        p.Vitals.Life = Math.Clamp(Life, 1, p.Vitals.MaxLife);
        p.Vitals.Mana = Math.Clamp(Mana, 0, p.Vitals.MaxMana);
        return p;
    }

    public string Serialize() => JsonSerializer.Serialize(this, SaveJson.Options);

    public static PlayerSave Deserialize(string json)
    {
        var node = JsonNode.Parse(json) as JsonObject ?? throw new InvalidDataException("Player save is not an object.");
        SaveMigrations.Default.Upgrade(node, FormatId, CurrentVersion);
        var save = node.Deserialize<PlayerSave>(SaveJson.Options) ?? throw new InvalidDataException("Empty player save.");
        if (save.Format != FormatId) throw new InvalidDataException($"Unexpected player format '{save.Format}'.");
        return save;
    }

    private static List<SlotModel> Slots(Inventory inv)
    {
        var list = new List<SlotModel>();
        for (int i = 0; i < inv.Size; i++)
        {
            var s = inv[i];
            if (!s.IsEmpty) list.Add(new SlotModel(i, s.Item!.Id, s.Count, s.Prefix?.Id, inv.IsFavorite(i)));
        }
        return list;
    }

    private static void Fill(Inventory inv, List<SlotModel>? slots, ContentDatabase content)
    {
        if (slots == null) return;
        foreach (var s in slots)
        {
            if (s.Slot < 0 || s.Slot >= inv.Size) continue;
            var item = content.GetOrAddPlaceholderItem(s.Item);
            var prefix = s.Prefix == null ? null : content.GetOrAddPlaceholderPrefix(s.Prefix);
            inv[s.Slot] = new ItemStack(item, s.Count, prefix);
            if (s.Favorite) inv.SetFavorite(s.Slot, true);
        }
    }
}
