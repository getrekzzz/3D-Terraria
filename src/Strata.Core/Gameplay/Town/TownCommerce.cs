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

/// <summary>An item on offer with its current price (copper).</summary>
public sealed record ShopOffer(ItemDef Item, long Price);

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
    private Pcg32 _rng;

    public TownCommerce(GameWorld world, TownSystem town)
    {
        _world = world;
        _town = town;
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

    /// <summary>Happiness score: liked/disliked biome at home and neighbours nearby.</summary>
    public int Mood(TownNpc npc, out int neighbours)
    {
        var biomes = _world.Session.ContextAt(npc.Home is { } h ? h + Common.Int3.Up : Physics.VoxelMotion.CellOf(npc.Center)).Biomes;
        var near = _town.Residents.Where(o => o != npc && o.Alive && System.Numerics.Vector3.Distance(o.Position, npc.Position) <= Config.NeighborRadius).ToList();
        neighbours = near.Count;
        int score = 0;
        foreach (var m in npc.Def.Mood)
        {
            if (m.Biome != null && biomes.Contains(m.Biome)) score += m.Score;
            if (m.Neighbor != null && near.Any(o => o.Def.Id == m.Neighbor)) score += m.Score;
        }
        return score;
    }

    /// <summary>Price multiplier from mood and crowding, clamped to the configured range.</summary>
    public float PriceFactor(TownNpc npc)
    {
        int mood = Mood(npc, out int neighbours);
        float factor = 1f - mood * Config.MoodStep + Math.Max(0, neighbours - Config.CrowdLimit) * Config.CrowdStep;
        if (npc.Home == null) factor += Config.CrowdStep * 2f; // homeless residents charge more
        return Math.Clamp(factor, Config.MinPriceFactor, Config.MaxPriceFactor);
    }

    /// <summary>What the resident sells right now.</summary>
    public List<ShopOffer> Stock(TownNpc npc, PlayerEntity player)
    {
        var ctx = ContextFor(npc, player);
        float factor = PriceFactor(npc);
        return npc.Def.Shop
            .Where(e => Condition(e.Conditions).Evaluate(ctx))
            .Select(e => new ShopOffer(Content.Items.Get(e.Item), Math.Max(1, (long)MathF.Round((e.Price ?? Content.Items.Get(e.Item).Value) * factor))))
            .ToList();
    }

    public TradeResult Buy(TownNpc npc, PlayerEntity player, ItemDef item, int count = 1)
    {
        var offer = Stock(npc, player).FirstOrDefault(o => o.Item == item);
        if (offer == null) return TradeResult.Unavailable;
        var inv = player.State.Inventory;
        if (!inv.CanTake(new ItemStack(item, count))) return TradeResult.NoRoom;
        if (!Wallet.TrySpend(inv, player.State.BanksForPaying(), Content, offer.Price * count, out var change)) return TradeResult.TooPoor;
        // Bought weapons and accessories come with a prefix too, each one rolled separately.
        var rest = Prefixes.Made(item, count, Content, ref _rng).Select(g => inv.Pickup(g)).ToList();
        foreach (var s in change.Concat(rest).Where(s => !s.IsEmpty)) _world.Drops.Spawn(s, player.Center, System.Numerics.Vector3.Zero);
        return TradeResult.Ok;
    }

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
        if (npc.Def.Service is not { Type: TownServiceSpecs.Heal } spec) return 0;
        var p = spec.Parse<HealServiceParams>();
        var v = player.State.Vitals;
        int missing = v.MaxLife - v.Life;
        int debuffs = p.ClearsDebuffs ? player.State.Buffs.Active.Count(b => b.Def.Debuff && !b.Def.Persistent) : 0;
        if (missing <= 0 && debuffs == 0) return 0;
        return Math.Max(1, (long)MathF.Round((missing + debuffs * 10) * p.CostPerLife * PriceFactor(npc)));
    }

    public TradeResult Heal(TownNpc npc, PlayerEntity player)
    {
        if (npc.Def.Service is not { Type: TownServiceSpecs.Heal } spec) return TradeResult.Unavailable;
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
        if (npc.Def.Service is not { Type: TownServiceSpecs.SummonBoss } spec || !npc.Alive) return TradeResult.Unavailable;
        var p = spec.Parse<SummonServiceParams>();
        var boss = Content.Enemies.Get(p.Enemy);
        if (_world.Npcs.All.Any(n => n.Def == boss && n.Alive)) return TradeResult.NothingToDo;
        if (!Condition(p.Conditions).Evaluate(ContextFor(npc, player)))
        {
            message = p.FailMessage;
            return TradeResult.Refused;
        }
        if (_world.Npcs.Spawn(boss, npc.Position + new System.Numerics.Vector3(0, p.Height, 0)) == null) return TradeResult.Unavailable;
        if (p.Transforms) _town.Vanish(npc);
        return TradeResult.Ok;
    }

    /// <summary>Recipes that use an item (the guide's crafting help), as output item names with their stations.</summary>
    public List<RecipeDef> RecipesUsing(ItemDef item)
    {
        var groups = Content.RecipeGroups.All.Where(g => g.Items.Contains(item.Id)).Select(g => g.Id).ToHashSet();
        return Content.Recipes.All.Where(r => r.Inputs.Any(i => i.Item == item.Id || (i.Group != null && groups.Contains(i.Group)))).ToList();
    }

    private ICondition Condition(TypedSpec[] specs)
    {
        if (!_compiled.TryGetValue(specs, out var c)) _compiled[specs] = c = ConditionCompiler.CompileAll(specs);
        return c;
    }
}
