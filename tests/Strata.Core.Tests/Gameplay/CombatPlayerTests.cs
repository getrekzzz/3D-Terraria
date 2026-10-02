using Strata.Core.Content.Defs;
using Strata.Core.Determinism;
using Strata.Core.Gameplay.Combat;
using Strata.Core.Gameplay.Items;
using Strata.Core.Gameplay.Player;
using Strata.Core.Gameplay.Stats;
using Strata.Core.Persistence;
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
}
