using System.Numerics;
using Strata.Core.Common;
using Strata.Core.Content.Loading;
using Strata.Core.Determinism;
using Strata.Core.Gameplay.Entities;
using Strata.Core.Gameplay.Fishing;
using Strata.Core.Gameplay.Interaction;
using Strata.Core.Gameplay.Items;
using Strata.Core.Gameplay.Weather;
using Strata.Core.Tests.Support;
using Strata.Core.Voxels;

namespace Strata.Core.Tests.Gameplay.Fishing;

public class FishingPowerTests
{
    private static readonly FishingConfig Cfg = new();

    [Fact]
    public void ShippedConfigMatchesTheDefaults()
    {
        var shipped = FishingConfig.Load(new FileSystemDataSource(TestPaths.ProjectRoot));
        Assert.Equal(Cfg.RainMultiplier, shipped.RainMultiplier);
        Assert.Equal(Cfg.FullPoolCells, shipped.FullPoolCells);
        Assert.Equal(Cfg.MoonMultipliers, shipped.MoonMultipliers);
        Assert.Equal(Cfg.Rarities.Select(r => r.Id), shipped.Rarities.Select(r => r.Id));
        Assert.Equal(Cfg.Hours.Length, shipped.Hours.Length);
    }

    [Fact]
    public void WeatherTimeAndMoonMultiply()
    {
        // Full moon at an unmodified hour (8:00): only the moon's ×1.1.
        Assert.Equal(1.1f, FishingPower.Multiplier(Cfg, Precipitation.None, 8f, 0, null), 3);
        Assert.Equal(1.1f * 1.2f, FishingPower.Multiplier(Cfg, Precipitation.Rain, 8f, 0, null), 3);
        Assert.Equal(1.1f * 1.2f * 1.1f, FishingPower.Multiplier(Cfg, Precipitation.Storm, 8f, 0, null), 3);
        Assert.Equal(1.3f * 0.9f, FishingPower.Multiplier(Cfg, Precipitation.None, 5f, 4, null), 3); // dawn, new moon
        Assert.Equal(1.3f * 1.05f, FishingPower.Multiplier(Cfg, Precipitation.None, 19f, 1, null), 3); // dusk
        Assert.Equal(0.8f * 1.1f, FishingPower.Multiplier(Cfg, Precipitation.None, 12f, 0, null), 3); // midday sun
        var blood = new HashSet<string> { "blood_moon" };
        Assert.Equal(1.1f * 1.1f, FishingPower.Multiplier(Cfg, Precipitation.None, 22f, 0, blood), 3);
    }

    [Fact]
    public void PowerAddsRodBaitAndGearThenScalesByPool()
    {
        Assert.Equal(75, FishingPower.Compute(Cfg, 20, 50, 5, 1f, 1000));
        Assert.Equal(90, FishingPower.Compute(Cfg, 20, 50, 5, 1.2f, 1000));
        Assert.Equal(37, FishingPower.Compute(Cfg, 20, 50, 5, 1f, Cfg.FullPoolCells / 2)); // half a pool, half the power
        Assert.Equal(0, FishingPower.Compute(Cfg, 20, 50, 5, 1f, Cfg.MinPoolCells - 1)); // not enough water
        Assert.Equal(0, FishingPower.Compute(Cfg, 50, 0, 10, 1f, 1000)); // no bait, no bite
    }

    [Fact]
    public void StrongerBaitAndTackleAreUsedUpLessOften()
    {
        Assert.Equal(1f / (1f + 15f / 6f), FishingPower.BaitConsumeChance(Cfg, 15, 0), 4);
        Assert.True(FishingPower.BaitConsumeChance(Cfg, 50, 0) < FishingPower.BaitConsumeChance(Cfg, 15, 0));
        Assert.True(FishingPower.BaitConsumeChance(Cfg, 50, 1) < FishingPower.BaitConsumeChance(Cfg, 50, 0));
    }

    [Fact]
    public void HigherPowerRollsRarerCatches()
    {
        double Average(int power)
        {
            var rng = new Pcg32(7);
            long sum = 0;
            for (int i = 0; i < 20000; i++) sum += FishingPower.RollTier(Cfg, power, ref rng);
            return sum / 20000.0;
        }
        Assert.True(Average(80) > Average(20) * 1.5, "power barely matters");
        var r = new Pcg32(1);
        Assert.Equal(0, FishingPower.RollTier(Cfg, 0, ref r));
        Assert.Equal("rare", FishingPower.TierName(Cfg, 2));
        Assert.Equal("", FishingPower.TierName(Cfg, 0));
    }

    [Fact]
    public void PoolCountStopsAtTheCapAndAtOtherLiquids()
    {
        using var g = TestGame.Create();
        var batch = g.World.Session.World.BeginEdit();
        for (int x = 0; x < 5; x++)
            for (int z = 0; z < 5; z++)
            {
                batch.SetBlock(g.Floor.X + x, g.Floor.Y, g.Floor.Z + 4 + z, 0);
                batch.SetLiquid(g.Floor.X + x, g.Floor.Y, g.Floor.Z + 4 + z, LiquidCode.Full(x == 4 ? LiquidType.Lava : LiquidType.Water));
            }
        g.World.Session.Commit(batch);
        var start = new Int3(g.Floor.X, g.Floor.Y, g.Floor.Z + 4);
        var open = new Queue<Int3>();
        var seen = new HashSet<Int3>();
        Assert.Equal(20, FishingPower.CountPool(g.World.Session.World, start, LiquidType.Water, 300, open, seen));
        Assert.Equal(7, FishingPower.CountPool(g.World.Session.World, start, LiquidType.Water, 7, open, seen));
        Assert.Equal(0, FishingPower.CountPool(g.World.Session.World, start, LiquidType.Lava, 300, open, seen));
    }

    [Fact]
    public void GearAndBaitRaiseThePlayersFishingPower()
    {
        using var g = FishingRig.Pond(LiquidType.Water);
        var rod = g.Content.Items.Get("reinforced_fishing_pole");
        g.World.Session.Clock.SetHour(8f);
        Assert.Equal(0, g.World.Projectiles.FishingPowerFor(g.Player, rod, 1000)); // no bait
        g.Player.State.Inventory.Backpack.Add(new ItemStack(g.Content.Items.Get("worm"), 5));
        int withWorm = g.World.Projectiles.FishingPowerFor(g.Player, rod, 1000);
        g.Player.State.Inventory.Accessories.Add(new ItemStack(g.Content.Items.Get("angler_earring"), 1));
        g.Player.State.Recompute();
        int withEarring = g.World.Projectiles.FishingPowerFor(g.Player, rod, 1000);
        Assert.True(withWorm >= 45 * 0.9f && withWorm <= 45 * 1.1f, $"20 rod + 25 worm gave {withWorm}");
        Assert.True(withEarring > withWorm + 8, $"earring added {withEarring - withWorm}");
    }

    [Fact]
    public void CastingWithoutBaitIsRefused()
    {
        using var g = FishingRig.Pond(LiquidType.Water);
        g.Hold("wooden_fishing_pole");
        Assert.Equal(UseOutcome.Blocked, g.World.ItemUse.Use(g.Player, null, true));
        Assert.Null(g.World.Projectiles.BobberOf(g.Player));
    }

    [Fact]
    public void ACatchCanUseUpTheBait()
    {
        using var g = FishingRig.Pond(LiquidType.Water);
        g.World.Projectiles.Fishing = new FishingConfig { BaitDivisor = 1e9f }; // bait power does not save it: always used
        g.Hold("golden_fishing_rod");
        var worm = g.Content.Items.Get("worm");
        g.Player.State.Inventory.Backpack.Add(new ItemStack(worm, 3));
        var catches = FishingRig.CastAndReel(g);
        Assert.NotEmpty(catches);
        Assert.Equal(2, g.Player.State.Inventory.Backpack.Count(worm));
    }

    [Fact]
    public void LavaBurnsTheLineWithoutLavaproofGear()
    {
        using var g = FishingRig.Pond(LiquidType.Lava);
        g.Player.State.Inventory.Backpack.Add(new ItemStack(g.Content.Items.Get("master_bait"), 10));
        g.Hold("golden_fishing_rod");
        Assert.Equal(UseOutcome.Shot, g.World.ItemUse.Use(g.Player, null, true));
        for (int t = 0; t < 300 && g.World.Projectiles.BobberOf(g.Player) is { Floating: false }; t++) g.World.Tick();
        Assert.Null(g.World.Projectiles.BobberOf(g.Player));

        g.Player.UseCooldown = 0;
        g.Hold("hotline_fishing_hook");
        var catches = FishingRig.CastAndReel(g);
        Assert.Contains(catches, s => s.Item!.Id is "obsidifish" or "flarefin_koi" or "obsidian_crate" or "bottomless_lava_bucket" or "lavaproof_fishing_hook");
    }

    [Fact]
    public void QuestFishBiteOnlyWhileTheAnglerWantsThem()
    {
        using var g = FishingRig.Pond(LiquidType.Water);
        g.World.Town.ArrivalsEnabled = false;
        var angler = g.World.Town.Summon(g.Content.TownNpcs.Get("angler"),
            g.World.Navigator.NearestStandable(g.Floor + new Int3(-4, 1, -4), Strata.Core.Gameplay.Navigation.NavAgent.Humanoid, 4)!.Value);
        var wanted = g.World.Services.AnglerWants(angler)!;
        Assert.Equal(wanted.Id, g.World.Services.QuestFishToday());
        // A quest fish needs its biome too; force the uncommon tier and check the quest table directly.
        var ctx = g.World.Session.ContextAt(g.Floor);
        ctx.AnglerQuest = wanted.Id;
        ctx.FishingLiquid = "water";
        var rng = new Pcg32(3);
        var roller = new Strata.Core.Gameplay.Loot.LootRoller(g.Content);
        bool any = ctx.HasBiome("forest") && ctx.Layer == Strata.Core.World.WorldLayer.Surface;
        var got = roller.Roll(g.World.Projectiles.Fishing.QuestTable, ref rng, ctx);
        if (any && wanted.Id == "bunnyfish") Assert.Contains(got, s => s.Item == wanted);
        Assert.DoesNotContain(got, s => s.Item != wanted);
        ctx.AnglerQuest = "";
        Assert.Empty(roller.Roll(g.World.Projectiles.Fishing.QuestTable, ref rng, ctx));
    }

    [Fact]
    public void ABossBaitAloneStillDrawsABite()
    {
        using var g = FishingRig.Pond(LiquidType.Water);
        g.Hold("golden_fishing_rod");
        g.Player.State.Inventory.Backpack.Add(new Strata.Core.Gameplay.Items.ItemStack(g.Content.Items.Get("truffle_worm"), 1));
        g.Player.UseCooldown = 0;
        Assert.Equal(UseOutcome.Shot, g.World.ItemUse.Use(g.Player, null, true));
        var bobber = g.World.Projectiles.BobberOf(g.Player)!;
        bool bit = false;
        for (int t = 0; t < 3000 && !bit; t++)
        {
            g.World.Tick();
            bit = bobber.Biting;
        }
        Assert.True(bit, "a Truffle Worm on its own never drew a bite (Duke Fishron could not be fished up)");
    }

    [Fact]
    public void QuarterMoonsAreNeutral()
    {
        Assert.Equal(1f, FishingPower.Multiplier(Cfg, Precipitation.None, 12.5f, 2, null) / FishingPower.Multiplier(Cfg, Precipitation.None, 12.5f, 6, null), 3);
        Assert.Equal(1f, Cfg.MoonMultipliers[2], 3);
        Assert.Equal(1f, Cfg.MoonMultipliers[6], 3);
    }
}
