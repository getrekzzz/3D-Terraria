using System.Text.RegularExpressions;
using Strata.Core.Conditions;
using Strata.Core.Content;
using Strata.Core.Content.Defs;
using Strata.Core.Content.Specs;
using Strata.Core.Determinism;
using Strata.Core.Gameplay.Items;
using Strata.Core.Session;

namespace Strata.Core.Gameplay.Town;

public enum TradeResult
{
    Ok,
    TooPoor,
    NoRoom,
    NotSold,
    NothingToDo,
    Unavailable,

    /// <summary>The service's conditions do not hold right now (see the message).</summary>
    Refused,
}

/// <summary>An item on offer with its current price (copper, or a count of <see cref="Currency"/> when set).</summary>
public sealed record ShopOffer(ItemDef Item, long Price, ItemDef? Currency = null);

/// <summary>
/// Conversation and trade with residents: dialogue lines chosen by condition, shop stock and prices scaled by
/// happiness (liked biomes and neighbours make things cheaper, crowding dearer), selling at a fraction of value,
/// and services (healing for coins, crafting help).
/// </summary>
public sealed class TownCommerce
{
    private static readonly Regex Placeholder = new(@"\{(player|name|resident:([a-z0-9_]+))\}", RegexOptions.Compiled);

    private readonly GameWorld _world;
    private readonly TownSystem _town;
    private readonly Dictionary<TypedSpec[], ICondition> _compiled = new(ReferenceEqualityComparer.Instance);
    private readonly TownHappiness _happiness;
    private Pcg32 _rng;

    public TownCommerce(GameWorld world, TownSystem town)
    {
        _world = world;
        _town = town;
        _happiness = new TownHappiness(world, town);
        _rng = new Pcg32(Hash.Combine((ulong)world.Session.Settings.Seed, 0x54524144UL));
    }

    private TownConfig Config => _town.Config;
    private Content.ContentDatabase Content => _world.Session.Content;

    /// <summary>Context at the resident's home (or where they stand), with the player's wealth and the town.</summary>
    public ConditionContext ContextFor(TownNpc npc, PlayerEntity player)
    {
        var ctx = _town.TownContext(player);
        var local = _world.Session.ContextAt(npc.Home is { } h ? h + Common.Int3.Up : Physics.VoxelMotion.CellOf(npc.Center));
        ctx.Biomes = local.Biomes;
        ctx.Layer = local.Layer;
        return ctx;
    }

    /// <summary>A line to say to the player, placeholders filled in.</summary>
    public string Greet(TownNpc npc, PlayerEntity player)
    {
        if (npc.Bound && npc.Def.Rescue is { } rescue)
            return rescue.RequiresItem != null && Content.Items.TryGet(rescue.RequiresItem, out var needed)
                ? rescue.NeedsItemText.Replace("{item}", needed.DisplayName) : "Help!";
        var ctx = ContextFor(npc, player);
        var lines = npc.Def.Lines.Where(l => Condition(l.Conditions).Evaluate(ctx)).ToList();
        if (lines.Count == 0) return "...";
        var line = lines[_rng.Range(0, lines.Count - 1)];
        return Placeholder.Replace(line.Text, m => m.Groups[1].Value switch
        {
            "player" => player.State.Name,
            "name" => npc.Name,
            _ => _town.Find(m.Groups[2].Value)?.Name ?? Content.TownNpcs.Get(m.Groups[2].Value).DisplayName,
        });
    }

    /// <summary>Terraria happiness (biomes, neighbours, crowding, solitude): a report with the price factor and reasons.</summary>
    public HappinessReport Happiness(TownNpc npc) => _happiness.Evaluate(npc);

    /// <summary>What the resident says about how they feel.</summary>
    public string DescribeHappiness(TownNpc npc) => _happiness.Describe(npc, _happiness.Evaluate(npc));

    /// <summary>Price multiplier from happiness, clamped to the configured range.</summary>
    public float PriceFactor(TownNpc npc) => _happiness.Evaluate(npc).Factor;

    /// <summary>The resident's shop tabs ("shop", "decor"…), in data order.</summary>
    public IReadOnlyList<string> Tabs(TownNpc npc) => npc.Def.ShopTabs;

    /// <summary>What the resident sells right now under a tab. Coin prices scale with happiness; currency prices do not.</summary>
    public List<ShopOffer> Stock(TownNpc npc, PlayerEntity player, string tab = ShopTabs.Shop)
    {
        var ctx = ContextFor(npc, player);
        float factor = PriceFactor(npc);
        var offers = new List<ShopOffer>();
        foreach (var e in npc.Def.Visit == null ? npc.Def.Shop : npc.Def.Shop.Concat(VisitorStock(npc)))
        {
            if (e.Tab != tab || !Condition(e.Conditions).Evaluate(ctx)) continue;
            var item = Content.Items.Get(e.Item);
            offers.Add(e.Currency is { } cur
                ? new ShopOffer(item, Math.Max(1, e.Price ?? 1), Content.Items.Get(cur))
                : new ShopOffer(item, Math.Max(1, (long)MathF.Round((e.Price ?? item.Value) * factor))));
        }
        return offers;
    }

    private (long Npc, long Day, List<ShopEntry> Entries) _visitStock = (-1, -1, new());

    /// <summary>
    /// A visitor's rotating stock for this visit: each pool's <see cref="ShopPool.Count"/> entries drawn with
    /// Pcg32(world seed, visit day, id), so it holds for the whole visit and changes the next. Conditions apply as usual.
    /// </summary>
    public List<ShopEntry> VisitorStock(TownNpc npc)
    {
        if (npc.Def.Visit is not { } visit) return new List<ShopEntry>();
        if (_visitStock.Npc == npc.Id && _visitStock.Day == npc.VisitDay) return _visitStock.Entries;
        var rng = new Determinism.Pcg32(Determinism.Hash.Combine(Determinism.Hash.Combine((ulong)_world.Session.Settings.Seed, npc.VisitDay), Determinism.Hash.String(npc.Def.Id)));
        var entries = new List<ShopEntry>();
        var bag = new List<ShopEntry>();
        foreach (var pool in visit.Pools)
        {
            bag.Clear();
            bag.AddRange(pool.Entries);
            for (int i = 0; i < pool.Count && bag.Count > 0; i++)
            {
                int k = rng.NextInt(bag.Count);
                entries.Add(bag[k]);
                bag.RemoveAt(k);
            }
        }
        _visitStock = (npc.Id, npc.VisitDay, entries);
        return entries;
    }

    public TradeResult Buy(TownNpc npc, PlayerEntity player, ItemDef item, int count = 1, string tab = ShopTabs.Shop)
    {
        var offer = Stock(npc, player, tab).FirstOrDefault(o => o.Item == item);
        if (offer == null) return TradeResult.Unavailable;
        var inv = player.State.Inventory;
        if (!inv.CanTake(new ItemStack(item, count))) return TradeResult.NoRoom;
        IEnumerable<ItemStack> change = Array.Empty<ItemStack>();
        if (offer.Currency is { } currency)
        {
            long cost = offer.Price * count;
            if (inv.Backpack.Count(currency) < cost) return TradeResult.TooPoor;
            inv.Backpack.Remove(d => d == currency, (int)cost);
        }
        else if (!Wallet.TrySpend(inv, player.State.BanksForPaying(), Content, offer.Price * count, out var coins)) return TradeResult.TooPoor;
        else change = coins;
        // Bought weapons and accessories come with a prefix too, each one rolled separately.
        var rest = Prefixes.Made(item, count, Content, ref _rng).Select(g => inv.Pickup(g)).ToList();
        foreach (var s in change.Concat(rest).Where(s => !s.IsEmpty)) _world.Drops.Spawn(s, player.Center, System.Numerics.Vector3.Zero);
        Traded?.Invoke(player, item, count);
        return TradeResult.Ok;
    }

    /// <summary>A player bought goods from a resident (item, count) — for the journal.</summary>
    public event Action<PlayerEntity, ItemDef, int>? Traded;

    /// <summary>Sale value of a stack (copper).</summary>
    public long SaleValue(ItemStack stack) => Prefixes.SellValue(stack, Config.SellRatio);

    /// <summary>Sells the whole stack in a backpack slot.</summary>
    public TradeResult Sell(PlayerEntity player, int slot) => Sell(player, player.State.Inventory.Backpack, slot);

    /// <summary>Sells the whole stack in a slot of one of the player's storages (backpack or ammo; coins never sell).</summary>
    public TradeResult Sell(PlayerEntity player, Inventory from, int slot)
    {
        var inv = player.State.Inventory;
        var stack = from[slot];
        if (stack.IsEmpty || stack.Item!.HasTag("coin")) return TradeResult.NotSold;
        long value = SaleValue(stack);
        if (value <= 0) return TradeResult.NotSold;
        from.Take(slot, stack.Count);
        foreach (var s in Wallet.Give(inv, Content, value)) _world.Drops.Spawn(s, player.Center, System.Numerics.Vector3.Zero);
        return TradeResult.Ok;
    }

    /// <summary>Cost of the resident's healing service for this player now (0 = nothing to heal or no such service).</summary>
    public long HealCost(TownNpc npc, PlayerEntity player)
    {
        if (npc.Def.FindService(TownServiceSpecs.Heal) is not { } spec) return 0;
        var p = spec.Parse<HealServiceParams>();
        var v = player.State.Vitals;
        int missing = v.MaxLife - v.Life;
        int debuffs = p.ClearsDebuffs ? player.State.Buffs.Active.Count(b => b.Def.Debuff && !b.Def.Persistent) : 0;
        if (missing <= 0 && debuffs == 0) return 0;
        float tier = 1f;
        foreach (var t in p.Tiers)
            if (_world.Session.Flags.Contains(t.Flag))
            {
                tier = t.Multiplier;
                break;
            }
        float expert = _world.Session.Settings.EffectiveDifficulty is World.Difficulty.Expert or World.Difficulty.Master ? p.ExpertMultiplier : 1f;
        long baseCost = missing * (long)p.CostPerLife + debuffs * (long)(p.PerDebuff ?? p.CostPerLife * 10);
        return Math.Max(1, (long)MathF.Round(baseCost * tier * expert * PriceFactor(npc)));
    }

    public TradeResult Heal(TownNpc npc, PlayerEntity player)
    {
        if (npc.Def.FindService(TownServiceSpecs.Heal) is not { } spec) return TradeResult.Unavailable;
        long cost = HealCost(npc, player);
        if (cost == 0) return TradeResult.NothingToDo;
        if (!Wallet.TrySpend(player.State.Inventory, player.State.BanksForPaying(), Content, cost, out var change)) return TradeResult.TooPoor;
        foreach (var s in change) _world.Drops.Spawn(s, player.Center, System.Numerics.Vector3.Zero);
        var state = player.State;
        if (spec.Parse<HealServiceParams>().ClearsDebuffs)
            foreach (var b in state.Buffs.Active.Where(b => b.Def.Debuff && !b.Def.Persistent).ToList()) state.Buffs.Remove(b.Def.Id);
        state.Recompute();
        _world.ReportHeal(player, state.Vitals.Heal(state.Vitals.MaxLife), mana: false);
        return TradeResult.Ok;
    }

    /// <summary>
    /// The resident's boss-summoning service (the keeper's curse): when its conditions hold around the resident,
    /// the boss appears above them, and a transforming resident vanishes into it.
    /// </summary>
    public TradeResult Summon(TownNpc npc, PlayerEntity player, out string? message)
    {
        message = null;
        if (npc.Def.FindService(TownServiceSpecs.SummonBoss) is not { } spec || !npc.Alive) return TradeResult.Unavailable;
        var p = spec.Parse<SummonServiceParams>();
        var boss = Content.Enemies.Get(p.Enemy);
        if (_world.Npcs.All.Any(n => n.Def == boss && n.Alive)) return TradeResult.NothingToDo;
        if (!Condition(p.Conditions).Evaluate(ContextFor(npc, player)))
        {
            message = p.FailMessage;
            return TradeResult.Refused;
        }
        if (p.Transforms) return Curse(npc, boss, p.Height) ? TradeResult.Ok : TradeResult.Unavailable;
        return _world.Npcs.Spawn(boss, npc.Position + new System.Numerics.Vector3(0, p.Height, 0)) != null ? TradeResult.Ok : TradeResult.Unavailable;
    }

    /// <summary>
    /// A resident is cursed into a boss (the keeper's curse, the Clothier Voodoo Doll): the boss appears
    /// <paramref name="height"/> m above them and they vanish into it, returning after the respawn delay.
    /// </summary>
    public bool Curse(TownNpc npc, EnemyDef boss, float height)
    {
        if (!npc.Alive || _world.Npcs.Spawn(boss, npc.Position + new System.Numerics.Vector3(0, height, 0)) == null) return false;
        _town.Vanish(npc);
        return true;
    }

    /// <summary>Recipes that use an item (the guide's crafting help). Kept for the current TownDialog until the W4 dialog
    /// shows <see cref="Crafting.RecipeLookup"/> views; now answered from the recipe book's ingredient index.</summary>
    public List<RecipeDef> RecipesUsing(ItemDef item) => _world.Recipes.RecipesUsing(item).Select(r => r.Def).ToList();

    private ICondition Condition(TypedSpec[] specs)
    {
        if (!_compiled.TryGetValue(specs, out var c)) _compiled[specs] = c = ConditionCompiler.CompileAll(specs);
        return c;
    }
}
