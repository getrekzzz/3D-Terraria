using Strata.Core.Content.Defs;
using Strata.Core.Determinism;
using Strata.Core.Common;
using Strata.Core.Gameplay.Combat;
using Strata.Core.Gameplay.Items;
using Strata.Core.Gameplay.Player;
using Strata.Core.Gameplay.Stats;
using Strata.Core.Gameplay.Town;
using Strata.Core.Gameplay.Interaction;
using Strata.Core.Persistence;
using Strata.Core.Presentation;
using Strata.Core.Tests.Support;

namespace Strata.Core.Tests.Gameplay;

public class CombatPlayerTests
{
    [Fact]
    public void DefenseCritAndVarianceFollowTheModel()
    {
        var rng = new Pcg32(1);
        int min = int.MaxValue, max = 0;
        for (int i = 0; i < 2000; i++)
        {
            var hit = DamageModel.Roll(20, DamageClass.Melee, 0, 5f, null, 10, 0.5f, 0f, 0.5f, ref rng);
            if (hit.Crit) continue;
            min = Math.Min(min, hit.Damage);
            max = Math.Max(max, hit.Damage);
            Assert.Equal(2.5f, hit.Knockback, 3);
        }
        Assert.InRange(min, 12, 13); // 20*0.85 - 5
        Assert.InRange(max, 18, 18); // 20*1.15 - 5
        var always = DamageModel.Roll(10, DamageClass.Melee, 100, 0, null, 0, 0.5f, 0, 0, ref rng);
        Assert.True(always.Crit);
        Assert.InRange(always.Damage, 17, 23);
        Assert.Equal(1, DamageModel.Roll(1, DamageClass.Melee, 0, 0, null, 999, 1f, 0, 0, ref rng).Damage);
    }

    [Fact]
    public void ImmunityFramesBlockRepeatedHits()
    {
        var v = new Vitals(100, 20);
        Assert.True(v.Hurt(10));
        Assert.False(v.Hurt(10));
        for (int i = 0; i < 40; i++) v.Tick(0);
        Assert.True(v.Hurt(10));
        Assert.Equal(80, v.Life);
    }

    [Fact]
    public void EquipmentEffectsApply()
    {
        var db = TestPaths.Content;
        var p = PlayerState.CreateNew("hero", db);
        p.Inventory.Backpack.Add(new ItemStack(db.Items.Get("hermes_boots"), 1));
        p.Inventory.Backpack.Add(new ItemStack(db.Items.Get("lucky_horseshoe"), 1));
        p.Inventory.Backpack.Add(new ItemStack(db.Items.Get("copper_helmet"), 1));
        for (int i = 0; i < p.Inventory.Backpack.Size; i++)
            if (!p.Inventory.Backpack[i].IsEmpty && p.Inventory.Backpack[i].Item!.EquipSlot != EquipSlot.None) p.Inventory.Equip(i);
        p.Recompute();
        Assert.Equal(0.45f, p.Effects.Stats.Bonus(StatIds.RunSpeed), 3);
        Assert.True(p.Effects.NoFallDamage);
        Assert.Equal(1, p.Defense);
    }

    [Fact]
    public void BuffsExpireAndAffectStats()
    {
        var db = TestPaths.Content;
        var p = PlayerState.CreateNew("hero", db);
        p.Buffs.Add(db.Buffs.Get("ironskin"), 3);
        p.Recompute();
        Assert.Equal(8, p.Defense);
        p.Buffs.Tick(); p.Buffs.Tick(); p.Buffs.Tick();
        p.Recompute();
        Assert.Equal(0, p.Defense);
    }

    [Fact]
    public void PlayerSaveRoundTrips()
    {
        var db = TestPaths.Content;
        var p = PlayerState.CreateNew("hero", db);
        p.BonusLife = 40;
        p.Inventory.Backpack[20] = new ItemStack(db.Items.Get("iron_broadsword"), 1, db.Prefixes.Get("legendary"));
        p.SpawnPoints["dev"] = new[] { 1, 2, 3 };
        p.Recompute();
        p.Vitals.Life = 77;
        var json = PlayerSave.From(p).Serialize();
        var back = PlayerSave.Deserialize(json).ToState(db);
        Assert.Equal(140, back.Vitals.MaxLife);
        Assert.Equal(77, back.Vitals.Life);
        Assert.Equal("legendary", back.Inventory.Backpack[20].Prefix!.Id);
        Assert.Equal(new[] { 1, 2, 3 }, back.SpawnPoints["dev"]);
        Assert.Equal(p.Inventory.Backpack.Stacks.Count(), back.Inventory.Backpack.Stacks.Count());
    }

    private static List<(int Amount, bool Mana)> Heals(TestGame g)
    {
        var heals = new List<(int, bool)>();
        g.World.PlayerHealed += (p, amount, mana) =>
        {
            Assert.Same(g.Player, p);
            heals.Add((amount, mana));
        };
        return heals;
    }

    [Fact]
    public void DrinkingAPotionReportsTheHealedAmount()
    {
        using var g = TestGame.Create();
        var heals = Heals(g);
        g.Player.State.Vitals.Life = 60;
        g.Hold("lesser_healing_potion");
        Assert.Equal(UseOutcome.Consumed, g.World.ItemUse.Use(g.Player, null, true));
        Assert.Equal(new[] { (40, false) }, heals); // 50 restores, but only 40 fit under the 100 maximum
        Assert.Equal(100, g.Player.State.Vitals.Life);
    }

    [Fact]
    public void ManaPotionReportsMana()
    {
        using var g = TestGame.Create();
        var heals = Heals(g);
        g.Player.State.Vitals.Mana = 5;
        g.Hold("lesser_mana_potion");
        Assert.Equal(UseOutcome.Consumed, g.World.ItemUse.Use(g.Player, null, true));
        Assert.Equal(new[] { (g.Player.State.Vitals.MaxMana - 5, true) }, heals);
    }

    [Fact]
    public void FullLifeReportsNothing()
    {
        using var g = TestGame.Create();
        var heals = Heals(g);
        g.Hold("lesser_healing_potion");
        Assert.Equal(UseOutcome.Consumed, g.World.ItemUse.Use(g.Player, null, true));
        Assert.Empty(heals);
        Assert.Equal(0, g.Player.State.Vitals.Heal(10));
        Assert.Equal(0, g.Player.State.Vitals.RestoreMana(10));
    }

    [Fact]
    public void CrystalsAndTheNurseReportHeals()
    {
        using var g = TestGame.Create();
        var heals = Heals(g);
        g.Hold("life_crystal");
        Assert.Equal(UseOutcome.Consumed, g.World.ItemUse.Use(g.Player, null, true));
        Assert.Equal(new[] { (20, false) }, heals); // the new 20 life arrives filled

        var at = g.World.Navigator.NearestStandable(g.Floor + new Int3(2, 1, 2), Strata.Core.Gameplay.Navigation.NavAgent.Humanoid, 3)!.Value;
        var nurse = g.World.Town.Summon(g.Content.TownNpcs.Get("nurse"), at);
        Assert.Empty(Wallet.Give(g.Player.State.Inventory, g.Content, 10_000));
        g.Player.State.Vitals.Life = 70;
        Assert.Equal(TradeResult.Ok, g.World.Commerce.Heal(nurse, g.Player));
        Assert.Equal((g.Player.State.Vitals.MaxLife - 70, false), heals[^1]);
    }

    [Fact]
    public void CombatTextPopsRisesAndFades()
    {
        Assert.Equal(CombatText.PopFrom, CombatText.Pop(0f), 3);
        Assert.Equal(1f, CombatText.Pop(CombatText.PopTime), 3);
        Assert.True(CombatText.Pop(CombatText.PopTime * 0.6f) > 1f); // the bounce overshoots
        Assert.Equal(0f, CombatText.Rise(0f));
        Assert.Equal(CombatText.RiseHeight, CombatText.Rise(CombatText.Lifetime), 3);
        float early = CombatText.Rise(0.1f) - CombatText.Rise(0f), late = CombatText.Rise(0.8f) - CombatText.Rise(0.7f);
        Assert.True(early > late * 3); // the rise slows down
        Assert.Equal(1f, CombatText.Alpha(CombatText.FadeStart));
        Assert.Equal(0f, CombatText.Alpha(CombatText.Lifetime));
        Assert.InRange(CombatText.Alpha(0.65f), 0.4f, 0.6f);

        Assert.Equal(new Rgb(255, 160, 80), CombatText.Color(CombatText.KindOf(crit: false, toPlayer: false)));
        Assert.Equal(new Rgb(255, 100, 30), CombatText.Color(CombatText.KindOf(crit: true, toPlayer: false)));
        Assert.Equal(new Rgb(255, 80, 90), CombatText.Color(CombatText.KindOf(crit: true, toPlayer: true)));
        Assert.Equal(new Rgb(100, 255, 100), CombatText.Color(CombatText.HealKind(mana: false)));
        Assert.Equal(new Rgb(100, 100, 255), CombatText.Color(CombatText.HealKind(mana: true)));
        Assert.Equal(1.25f, CombatText.Scale(CombatTextKind.Crit));
        Assert.Equal("+50", CombatText.Text(50, CombatTextKind.HealLife));
        Assert.Equal("12", CombatText.Text(12, CombatTextKind.Hostile));
        Assert.Same(CombatText.Text(12, CombatTextKind.Crit), CombatText.Text(12, CombatTextKind.ToPlayer)); // cached
        Assert.Equal("1234", CombatText.Text(1234, CombatTextKind.Hostile));
    }
}
