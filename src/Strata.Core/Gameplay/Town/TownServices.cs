using Strata.Core.Content.Specs;
using Strata.Core.Determinism;
using Strata.Core.Gameplay.Items;
using Strata.Core.Session;
using Strata.Core.Simulation;

namespace Strata.Core.Gameplay.Town;

/// <summary>
/// The residents' paid and quest services beyond healing and summoning: the Goblin Tinkerer's reforging, the Tax
/// Collector's rent and the Angler's daily fishing quest. State lives in the world's counters.
/// </summary>
public sealed class TownServices
{
    public const string TaxCounter = "taxes_hour";
    public const string AnglerCounter = "angler_done_day";

    private readonly GameWorld _world;
    private readonly TownCommerce _commerce;
    private Pcg32 _rng;

    public TownServices(GameWorld world, TownCommerce commerce)
    {
        _world = world;
        _commerce = commerce;
        _rng = new Pcg32(Hash.Combine((ulong)world.Session.Settings.Seed, 0x5345525649UL));
    }

    private Content.ContentDatabase Content => _world.Session.Content;
    private Dictionary<string, int> Counters => _world.Session.Counters;

    // ---- Reforging -------------------------------------------------------------------------------------------

    /// <summary>What reforging the held item costs now, or 0 when it cannot be reforged here.</summary>
    public long ReforgeCost(TownNpc npc, PlayerEntity player)
    {
        if (npc.Def.Service is not { Type: TownServiceSpecs.Reforge } spec) return 0;
        var held = player.State.Inventory.Held;
        if (held.IsEmpty || !Prefixes.CanHave(held.Item!)) return 0;
        float factor = spec.Parse<ReforgeServiceParams>().CostFactor * _commerce.PriceFactor(npc);
        return Math.Max(1, (long)MathF.Round(Prefixes.Value(held) * factor));
    }

    /// <summary>Re-rolls the held item's prefix (never the same one twice in a row).</summary>
    public TradeResult Reforge(TownNpc npc, PlayerEntity player, out ItemStack result)
    {
        result = ItemStack.Empty;
        long cost = ReforgeCost(npc, player);
        if (cost == 0) return TradeResult.NothingToDo;
        var inv = player.State.Inventory;
        if (!Wallet.TrySpend(inv, player.State.BanksForPaying(), Content, cost, out var change)) return TradeResult.TooPoor;
        foreach (var s in change) _world.Drops.Spawn(s, player.Center, System.Numerics.Vector3.Zero);
        var held = inv.Held;
        result = new ItemStack(held.Item!, held.Count, Prefixes.Roll(held.Item!, Content, ref _rng, held.Prefix));
        inv.Backpack[inv.SelectedHotbar] = result;
        player.State.Recompute();
        return TradeResult.Ok;
    }

    // ---- Taxes -----------------------------------------------------------------------------------------------

    private long Hour => _world.Session.Clock.Tick / GameClock.TicksPerHour;

    /// <summary>Rent collected since the last visit (copper), capped.</summary>
    public long TaxesDue(TownNpc npc)
    {
        if (npc.Def.Service is not { Type: TownServiceSpecs.Taxes } spec) return 0;
        var p = spec.Parse<TaxServiceParams>();
        if (!Counters.TryGetValue(TaxCounter, out int last))
        {
            Counters[TaxCounter] = (int)Hour; // the collector starts counting from the day he moves in
            return 0;
        }
        int housed = _world.Town.Residents.Count(r => r.IsHoused);
        return Math.Min(p.Cap, Math.Max(0, Hour - last) * housed * (long)p.PerResidentHour);
    }

    public TradeResult CollectTaxes(TownNpc npc, PlayerEntity player, out long amount)
    {
        amount = TaxesDue(npc);
        if (amount <= 0) return TradeResult.NothingToDo;
        foreach (var s in Wallet.Give(player.State.Inventory, Content, amount)) _world.Drops.Spawn(s, player.Center, System.Numerics.Vector3.Zero);
        Counters[TaxCounter] = (int)Hour;
        return TradeResult.Ok;
    }

    // ---- The Angler's quest ----------------------------------------------------------------------------------

    /// <summary>Today's wanted fish, or null (no quest service, or already done today).</summary>
    public Content.Defs.ItemDef? AnglerWants(TownNpc npc)
    {
        if (npc.Def.Service is not { Type: TownServiceSpecs.AnglerQuest } spec) return null;
        long day = _world.Session.Clock.Day;
        if (Counters.TryGetValue(AnglerCounter, out int done) && done == (int)day) return null;
        var fish = spec.Parse<AnglerQuestParams>().Fish;
        return Content.Items.Get(fish[(int)(day % fish.Length)]);
    }

    /// <summary>
    /// Id of the quest fish a living Angler wants today (null when no Angler lives here or today's quest is done):
    /// quest fish can only be caught while they are wanted.
    /// </summary>
    public string? QuestFishToday()
    {
        foreach (var npc in _world.Town.Residents)
            if (npc.Def.Service is { Type: TownServiceSpecs.AnglerQuest }) return AnglerWants(npc)?.Id;
        return null;
    }

    /// <summary>Hands in today's fish from the backpack for coins and a reward.</summary>
    public TradeResult TurnInFish(TownNpc npc, PlayerEntity player, out ItemStack reward)
    {
        reward = ItemStack.Empty;
        var wanted = AnglerWants(npc);
        if (wanted == null) return TradeResult.NothingToDo;
        var inv = player.State.Inventory;
        if (inv.Backpack.Count(wanted) < 1) return TradeResult.Unavailable;
        inv.Backpack.Remove(d => d == wanted, 1);
        Counters[AnglerCounter] = (int)_world.Session.Clock.Day;
        var p = npc.Def.Service!.Parse<AnglerQuestParams>();
        long coins = p.Coins.Length >= 2 ? _rng.Range((int)p.Coins[0], (int)p.Coins[1]) : p.Coins.FirstOrDefault();
        foreach (var s in Wallet.Give(inv, Content, coins)) _world.Drops.Spawn(s, player.Center, System.Numerics.Vector3.Zero);
        foreach (var r in p.Rewards)
        {
            if (!_rng.Chance(r.Chance) || !Content.Items.TryGet(r.Item, out var item)) continue;
            reward = Prefixes.Fresh(item, r.Count, Content, ref _rng);
            var rest = inv.Pickup(reward);
            if (!rest.IsEmpty) _world.Drops.Spawn(rest, player.Center, System.Numerics.Vector3.Zero);
            break;
        }
        return TradeResult.Ok;
    }
}
