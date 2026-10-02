using System.Numerics;
using Strata.Core.Common;
using Strata.Core.Content.Defs;
using Strata.Core.Content.Specs;
using Strata.Core.Gameplay.Fishing;
using Strata.Core.Gameplay.Items;
using Strata.Core.Gameplay.Loot;
using Strata.Core.Physics;
using Strata.Core.Session;
using Strata.Core.Voxels;
using StatIds = Strata.Core.Gameplay.Stats.StatIds;

namespace Strata.Core.Gameplay.Entities;

/// <summary>
/// Fishing bobbers: they float on water and honey (and lava with lavaproof gear), wait for a bite timed by the fishing
/// power (<see cref="FishingPower"/>), and on reeling land a quest fish, a crate or a fish rolled for the bobber's spot,
/// using up the bait now and then.
/// </summary>
public sealed partial class ProjectileSystem
{
    private readonly Queue<Int3> _poolOpen = new();
    private readonly HashSet<Int3> _poolSeen = new();
    private LootRoller? _loot;

    /// <summary>Fishing tuning (data/config/fishing.json; the game loads it, tests use the defaults).</summary>
    public FishingConfig Fishing { get; set; } = new();

    /// <summary>Catches landed so far this session (probes).</summary>
    public int Catches { get; private set; }

    /// <summary>The player's bobber, if one is out.</summary>
    public Projectile? BobberOf(PlayerEntity owner)
    {
        foreach (var x in _items)
            if (x.Owner == owner && x.Def.Bobber) return x;
        return null;
    }

    /// <summary>First bait in the backpack (hotbar first, as Terraria scans the inventory), or null.</summary>
    public BaitParams? FindBait(PlayerEntity owner, out int slot)
    {
        var backpack = owner.State.Inventory.Backpack;
        for (slot = 0; slot < backpack.Size; slot++)
            if (backpack[slot].Item is { } item && _world.Uses[item].Bait is { } bait) return bait;
        slot = -1;
        return null;
    }

    /// <summary>
    /// Whether the first bait in the backpack (hotbar first) is a boss bait such as the Truffle Worm: the line then bites
    /// even without ordinary bait, and the bite hooks the boss where its biome matches.
    /// </summary>
    public bool LeadsWithBossBait(PlayerEntity owner)
    {
        var backpack = owner.State.Inventory.Backpack;
        for (int i = 0; i < backpack.Size; i++)
        {
            if (backpack[i].Item is not { } item) continue;
            var u = _world.Uses[item];
            if (u.BaitSummon != null) return true;
            if (u.Bait != null) return false;
        }
        return false;
    }

    /// <summary>Whether the owner's gear lets a bobber rest in lava (a Hotline rod or a Lavaproof Fishing Hook).</summary>
    public bool CanFishLava(PlayerEntity owner, ItemDef? rod) =>
        rod != null && _world.Uses[rod].Fishing is { Lava: true } || owner.State.Effects.Stats.Bonus(StatIds.LavaFishing) > 0;

    /// <summary>The owner's fishing power with <paramref name="rod"/> in a pool of <paramref name="poolCells"/> cells now.</summary>
    public int FishingPowerFor(PlayerEntity owner, ItemDef? rod, int poolCells)
    {
        int rodPower = rod != null ? _world.Uses[rod].Fishing?.Power ?? 0 : 0;
        int bait = FindBait(owner, out _)?.Power ?? 0;
        float bonus = owner.State.Effects.Stats.Bonus(StatIds.FishingPower);
        var s = _world.Session;
        float m = FishingPower.Multiplier(Fishing, s.Weather.Precipitation, s.Clock.Hour, s.Clock.MoonPhase, s.Events);
        return FishingPower.Compute(Fishing, rodPower, bait, bonus, m, poolCells);
    }

    private bool TickBobber(Projectile p, PlayerEntity owner, float dt)
    {
        var world = _world.Session.World;
        if (Vector3.DistanceSquared(owner.Center, p.Position) > 40f * 40f) return false; // the line snaps
        var cell = VoxelMotion.CellOf(p.Position);
        var liquid = LiquidCode.Type(world.GetLiquid(cell.X, cell.Y, cell.Z));
        if (liquid == LiquidType.Lava && !CanFishLava(owner, p.Source)) return false; // the line burns through
        bool fishable = liquid is LiquidType.Water or LiquidType.Honey or LiquidType.Lava;
        if (!p.Floating)
        {
            p.Velocity.Y -= p.Def.Gravity * dt;
            if (fishable)
            {
                p.Floating = true;
                p.Velocity = Vector3.Zero;
                p.FishLiquid = liquid;
                p.PoolCells = FishingPower.CountPool(world, cell, liquid, Fishing.FullPoolCells, _poolOpen, _poolSeen);
                ScheduleBite(p);
                return true;
            }
            float r = MathF.Min(p.Def.Radius * Units.CellSize, 0.2f);
            VoxelMotion.Move(world, ref p.Position, ref p.Velocity, new Vector3(r), dt);
            p.Velocity.X *= 0.99f;
            p.Velocity.Z *= 0.99f;
            return true;
        }
        if (!fishable)
        {
            // Drifted out of (or drained from under) the liquid: sink again.
            p.Floating = false;
            p.Biting = false;
            return true;
        }
        // Rise to the surface of the liquid column and rest there.
        var above = cell + new Int3(0, 1, 0);
        bool submerged = LiquidCode.Type(world.GetLiquid(above.X, above.Y, above.Z)) != LiquidType.None;
        float level = (world.GetLiquid(cell.X, cell.Y, cell.Z) & 0x1F) / (float)LiquidCode.MaxLevel;
        float surface = submerged ? p.Position.Y + 0.5f : (cell.Y + level) * Units.CellSize;
        p.Position.Y += MathF.Min(surface - p.Position.Y - 0.02f, 2f * dt);
        if (!p.Biting && p.Age >= p.BiteAt)
        {
            if (p.FishingPower <= 0) ScheduleBite(p); // no bait or too little liquid: look again in a while
            else
            {
                p.Biting = true;
                p.BiteAt = p.Age + 50; // the fish holds on for a moment
            }
        }
        else if (p.Biting && p.Age >= p.BiteAt)
        {
            p.Biting = false;
            ScheduleBite(p);
        }
        return true;
    }

    /// <summary>Times the next bite: higher fishing power bites sooner; without power, checks again in two seconds.</summary>
    private void ScheduleBite(Projectile p)
    {
        p.FishingPower = p.Owner != null ? FishingPowerFor(p.Owner, p.Source, p.PoolCells) : 0;
        if (p.FishingPower <= 0 && p.Owner != null && LeadsWithBossBait(p.Owner)) p.FishingPower = 1; // a boss bait still draws a bite
        if (p.FishingPower <= 0)
        {
            p.BiteAt = p.Age + 120;
            return;
        }
        float wait = _rng.Range(90, 420) * 25f / (p.FishingPower + 20f);
        p.BiteAt = p.Age + (int)wait;
    }

    /// <summary>
    /// Reels the bobber in. A biting fish yields, by the rolled catch rarity: today's quest fish (if it lives here), a
    /// crate (<see cref="FishingConfig.CrateChance"/> plus the Crate Potion), or a catch from the liquid's table; each
    /// table falls back from the rolled rarity to commoner ones. The bait may be used up. Otherwise nothing.
    /// </summary>
    public List<ItemStack> Reel(Projectile bobber)
    {
        var catches = new List<ItemStack>();
        int index = _items.IndexOf(bobber);
        if (index < 0) return catches;
        if (bobber.Biting && bobber.Owner is { } owner && FindBait(owner, out int baitSlot) is { } bait)
        {
            var cell = VoxelMotion.CellOf(bobber.Position);
            var ctx = _world.Session.ContextAt(cell);
            int power = FishingPowerFor(owner, bobber.Source, bobber.PoolCells);
            ctx.FishingPower = power;
            ctx.FishingLiquid = bobber.FishLiquid.ToString().ToLowerInvariant();
            ctx.AnglerQuest = _world.Services.QuestFishToday() ?? "";
            ctx.Random = _rng;
            _loot ??= new LootRoller(_world.Session.Content);
            int tier = FishingPower.RollTier(Fishing, power, ref _rng);
            bool lava = bobber.FishLiquid == LiquidType.Lava;
            if (tier >= Fishing.QuestTier && ctx.AnglerQuest.Length > 0) RollTiered(Fishing.QuestTable, 0, ctx, catches);
            if (catches.Count == 0 && _rng.NextFloat() < Fishing.CrateChance + owner.State.Effects.Stats.Bonus(StatIds.CrateChance))
                RollTiered(lava ? Fishing.LavaCrateTable : Fishing.CrateTable, tier, ctx, catches);
            if (catches.Count == 0)
                RollTiered(lava ? Fishing.LavaTable : bobber.FishLiquid == LiquidType.Honey ? Fishing.HoneyTable : Fishing.WaterTable, tier, ctx, catches);
            if (catches.Count > 0) Catches++;
            float use = FishingPower.BaitConsumeChance(Fishing, bait.Power, owner.State.Effects.Stats.Bonus(StatIds.BaitSaving));
            if (catches.Count > 0 && _rng.NextFloat() < use) owner.State.Inventory.Backpack.Take(baitSlot, 1);
        }
        End(index);
        return catches;
    }

    /// <summary>Rolls <c>table_&lt;tier&gt;</c>, falling back tier by tier to the plain table until something comes up.</summary>
    private void RollTiered(string table, int tier, Conditions.ConditionContext ctx, List<ItemStack> into)
    {
        var tables = _world.Session.Content.LootTables;
        for (int t = tier; t >= 0 && into.Count == 0; t--)
        {
            string id = t == 0 ? table : table + "_" + FishingPower.TierName(Fishing, t);
            if (tables.Contains(id)) into.AddRange(_loot!.Roll(id, ref _rng, ctx));
        }
    }
}
