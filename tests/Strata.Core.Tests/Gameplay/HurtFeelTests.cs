using System.Numerics;
using Strata.Core.Content.Defs;
using Strata.Core.Gameplay.Entities;
using Strata.Core.Tests.Support;

namespace Strata.Core.Tests.Gameplay;

/// <summary>Hurt feedback: debuff and fall damage are announced like hits; knocked creatures hop back.</summary>
public class HurtFeelTests
{
    [Fact]
    public void BurningAnnouncesOnePeriodicHurtPerSecond()
    {
        using var g = TestGame.Create();
        g.World.Spawner.Enabled = false;
        var events = new List<DamageEvent>();
        g.World.Npcs.Damaged += e =>
        {
            if (e.ToPlayer) events.Add(e);
        };
        g.Player.State.Buffs.Add(g.Content.Buffs.Get("on_fire"), 600);
        g.Player.State.Recompute();
        int life = g.Player.State.Vitals.Life;
        g.Ticks(Strata.Core.Common.Units.TicksPerSecond * 3);
        Assert.InRange(events.Count, 2, 4);
        Assert.All(events, e => Assert.Equal(DamageKind.Periodic, e.Kind));
        Assert.Equal(life - g.Player.State.Vitals.Life, events.Sum(e => e.Amount));
    }

    [Fact]
    public void HurtPlayerDirectRaisesPlayerHurtWithoutKnockback()
    {
        using var g = TestGame.Create();
        var hurts = new List<(int Damage, Vector3 Knock)>();
        g.World.Npcs.PlayerHurt += (p, d, k) => hurts.Add((d, k));
        g.World.HurtPlayerDirect(g.Player, 12);
        var (damage, knock) = Assert.Single(hurts);
        Assert.Equal(12, damage);
        Assert.Equal(Vector3.Zero, knock);
    }

    [Fact]
    public void AGroundedZombieHopsBackWhenHit()
    {
        using var g = TestGame.Create();
        g.World.Spawner.Enabled = false;
        var zombie = g.World.Npcs.Spawn(g.Content.Enemies.Get("zombie"), g.Player.Position + new Vector3(4, 1, 0))!;
        for (int i = 0; i < 120 && !zombie.OnGround; i++) g.Ticks(1);
        Assert.True(zombie.OnGround);
        g.World.Npcs.Hit(zombie, null, 1, DamageClass.Melee, 0, 5f, g.Player.Center, melee: true);
        Assert.True(zombie.Velocity.Y >= NpcSystem.KnockHop, $"vy {zombie.Velocity.Y}");
        Assert.True(zombie.Velocity.X > 0f);
    }

    [Fact]
    public void HitsInOneTickDoNotStackTheHop()
    {
        using var g = TestGame.Create();
        g.World.Spawner.Enabled = false;
        var zombie = g.World.Npcs.Spawn(g.Content.Enemies.Get("zombie"), g.Player.Position + new Vector3(4, 1, 0))!;
        zombie.Life = zombie.MaxLife = 10000;
        for (int i = 0; i < 120 && !zombie.OnGround; i++) g.Ticks(1);
        for (int i = 0; i < 4; i++) g.World.Npcs.Hit(zombie, null, 1, DamageClass.Ranged, 0, 8f, g.Player.Center, melee: false);
        Assert.InRange(zombie.Velocity.Y, NpcSystem.KnockHop, NpcSystem.MaxKnockHop);
    }

    [Fact]
    public void FullKnockbackResistanceStandsFirm()
    {
        using var g = TestGame.Create();
        g.World.Spawner.Enabled = false;
        var knocks = new List<Vector3>();
        g.World.Npcs.PlayerHurt += (_, _, k) => knocks.Add(k);
        g.Player.State.Inventory.Accessories[0] = new Strata.Core.Gameplay.Items.ItemStack(g.Content.Items.Get("cobalt_shield"), 1);
        g.Player.State.Recompute();
        g.World.Npcs.Spawn(g.Content.Enemies.Get("zombie"), g.Player.Position + new Vector3(0.3f, 0.05f, 0));
        g.Ticks(30);
        Assert.NotEmpty(knocks);
        Assert.All(knocks, k => Assert.True(k.Length() < 1e-3f));
    }
}
