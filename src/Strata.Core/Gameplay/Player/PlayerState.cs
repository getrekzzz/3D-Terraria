using Strata.Core.Content;
using Strata.Core.Gameplay.Buffs;
using Strata.Core.Gameplay.Combat;
using Strata.Core.Gameplay.Items;
using Strata.Core.Gameplay.Stats;

namespace Strata.Core.Gameplay.Player;

/// <summary>
/// Everything that belongs to a player character (independent of any world): inventory, vitals,
/// permanent upgrades, buffs and derived effects.
/// </summary>
public sealed class PlayerState
{
    public const int BaseLife = 100;
    public const int BaseMana = 20;

    public PlayerState(string name)
    {
        Name = name;
        Vitals = new Vitals(BaseLife, BaseMana);
        // Whatever changes the gear (the inventory screen, commands, a death drop, loading) updates the stats at once.
        Inventory.EquipmentChanged += Recompute;
    }

    public string Name { get; set; }
    public PlayerInventory Inventory { get; } = new();
    public Vitals Vitals { get; }
    public BuffSet Buffs { get; } = new();
    public EffectState Effects { get; } = new();

    /// <summary>Slots in each personal storage (Piggy Bank, Safe), as in the reference game.</summary>
    public const int BankSize = 40;

    /// <summary>
    /// Personal storages carried with the character, keyed by <see cref="Content.Defs.TileObjectDef.SharedStorage"/>
    /// ("piggy_bank", "safe"): every placed object of that kind opens the same slots, in any world.
    /// </summary>
    public Dictionary<string, Inventory> Banks { get; } = new(StringComparer.Ordinal);

    /// <summary>The personal storage of that kind, created empty on first use.</summary>
    public Inventory Bank(string kind)
    {
        if (!Banks.TryGetValue(kind, out var bank)) Banks[kind] = bank = new Inventory(BankSize);
        return bank;
    }

    /// <summary>The storages a shop may take coins from after the purse, in the reference game's order (Piggy Bank, then Safe).</summary>
    public IEnumerable<Inventory> BanksForPaying() =>
        Banks.OrderBy(kv => kv.Key == "piggy_bank" ? 0 : kv.Key == "safe" ? 1 : 2).ThenBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Value);

    /// <summary>Permanent max-life bonus from vital crystals.</summary>
    public int BonusLife { get; set; }

    /// <summary>Permanent max-mana bonus from mana stars.</summary>
    public int BonusMana { get; set; }

    public int Deaths { get; set; }

    /// <summary>Demon Heart used: the sixth accessory slot is open while in an Expert or Master world.</summary>
    public bool ExtraAccessorySlot { get; set; }

    /// <summary>Minecart Upgrade Kit used: every minecart this player rides is the faster Mechanical Cart.</summary>
    public bool MinecartUpgrade { get; set; }

    public Appearance Look { get; set; } = new();

    /// <summary>The look changed in play (a haircut, a hair dye): the avatar re-applies it.</summary>
    public event Action? LookChanged;

    public void RaiseLookChanged() => LookChanged?.Invoke();

    public CharacterDifficulty Difficulty { get; set; }

    /// <summary>Time played with this character (ticks), shown in the character list.</summary>
    public long PlayTicks { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public DateTime LastPlayedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Respawn cell per world name (bed-set spawn points).</summary>
    public Dictionary<string, int[]> SpawnPoints { get; } = new(StringComparer.Ordinal);

    public int Defense { get; private set; }

    /// <summary>The active armor set bonus text (null unless a full set is worn).</summary>
    public string? SetBonus { get; private set; }

    /// <summary>Recomputes derived effects and stats from equipment and buffs (call after changes and each second).</summary>
    public void Recompute()
    {
        Effects.Clear();
        int defense = 0;
        foreach (var stack in Inventory.EquippedStacks)
        {
            var item = stack.Item!;
            defense += item.Defense;
            foreach (var e in item.EquipEffects) Effects.Apply(e);
            if (stack.Prefix is { } prefix && item.EquipSlot == Content.Defs.EquipSlot.Accessory)
                foreach (var (stat, add) in Items.Prefixes.AccessoryStats(prefix)) Effects.Stats.Add(stat, add, 1f);
        }
        SetBonus = null;
        foreach (var piece in Inventory.CompleteSet())
        {
            foreach (var e in piece.SetEffects) Effects.Apply(e);
            SetBonus ??= piece.SetBonus;
        }
        foreach (var b in Buffs.Active)
            foreach (var e in b.Def.Effects) Effects.Apply(e);
        Defense = (int)Effects.Stats.Get(StatIds.Defense, defense);
        Vitals.MaxLife = (int)Effects.Stats.Get(StatIds.MaxLife, BaseLife + BonusLife);
        Vitals.MaxMana = (int)Effects.Stats.Get(StatIds.MaxMana, BaseMana + BonusMana);
        Vitals.Life = Math.Min(Vitals.Life, Vitals.MaxLife);
        Vitals.Mana = Math.Min(Vitals.Mana, Vitals.MaxMana);
    }

    /// <summary>A new character's starting kit: the reference game's copper tools, plus torches for the first night.</summary>
    public static PlayerState CreateNew(string name, ContentDatabase content)
    {
        var p = new PlayerState(name);
        p.Inventory.Content = content;
        foreach (var (id, count) in new[] { ("copper_shortsword", 1), ("copper_pickaxe", 1), ("copper_axe", 1), ("torch", 25), ("wood", 20) })
            if (content.Items.TryGet(id, out var item)) p.Inventory.Backpack.Add(new ItemStack(item, count));
        p.Recompute();
        return p;
    }
}
