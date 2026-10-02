using System.Numerics;
using Strata.Core.Common;
using Strata.Core.Content.Specs;
using Strata.Core.Gameplay.Town;
using Strata.Core.Tests.Support;

namespace Strata.Core.Tests.Gameplay;

/// <summary>P5-10: residents fight back: shots, no shots through walls, the Nurse heals, the Dryad wards.</summary>
public class TownFightBackTests
{
    private static TestGame Create()
    {
        var g = TestGame.Create();
        g.World.Spawner.Enabled = false;
        g.World.Town.ArrivalsEnabled = false;
        g.World.Session.Clock.SetHour(22f);
        return g;
    }

    private static Vector3 Feet(TestGame g, float x) => TownSystem.NodeFeet(g.Floor + Int3.Up) + new Vector3(x, 0, 0);

    private static TownNpc Summon(TestGame g, string id, float x) =>
        g.World.Town.Summon(g.Content.TownNpcs.Get(id), g.World.Navigator.NearestStandable(g.Floor + Int3.Up + new Int3((int)(x / Units.CellSize), 0, 0), Strata.Core.Gameplay.Navigation.NavAgent.Humanoid, 2)!.Value);

    private static void Run(TestGame g, int ticks, Vector3 playerAt, Func<bool>? until = null)
    {
        for (int i = 0; i < ticks && until?.Invoke() != true; i++)
        {
            g.Player.Position = playerAt;
            g.World.Tick();
        }
    }

    [Fact]
    public void GuideShootsZombie()
    {
        using var g = Create();
        var guide = Summon(g, "guide", 0f);
        var zombie = g.World.Npcs.Spawn(g.Content.Enemies.Get("zombie"), Feet(g, 10f))!;
        Run(g, 400, Feet(g, -30f), () => zombie.Life < zombie.MaxLife);
        Assert.True(guide.Attacks > 0);
        Assert.True(zombie.Life < zombie.MaxLife);
        Assert.Equal(guide.Def.Life, guide.Life);
    }

    [Fact]
    public void NoShotsThroughWalls()
    {
        using var g = Create();
        var guide = Summon(g, "guide", 0f);
        ushort stone = (ushort)g.Content.Blocks.Get("stone").RuntimeId;
        int wx = g.Floor.X + (int)(4f / Units.CellSize);
        for (int y = 1; y <= 10; y++)
            for (int z = -8; z <= 8; z++)
                g.World.Editor.SetBlock(new Int3(wx, g.Floor.Y + y, g.Floor.Z + z), stone);
        var zombie = g.World.Npcs.Spawn(g.Content.Enemies.Get("zombie"), Feet(g, 9f))!;
        Run(g, 240, Feet(g, -30f));
        Assert.Equal(0, guide.Attacks);
        Assert.Equal(zombie.MaxLife, zombie.Life);
    }

    [Fact]
    public void NurseHealsHurtResident()
    {
        using var g = Create();
        Summon(g, "nurse", 0f);
        var merchant = Summon(g, "merchant", 2f);
        merchant.Life = merchant.Def.Life / 2;
        g.World.Npcs.Spawn(g.Content.Enemies.Get("zombie"), Feet(g, 12f));
        int before = merchant.Life;
        Run(g, 120, Feet(g, -30f), () => merchant.Life > before);
        Assert.True(merchant.Life > before);
    }

    [Fact]
    public void DryadWardBuffsPlayer()
    {
        using var g = Create();
        var dryad = Summon(g, "dryad", 0f);
        g.World.Npcs.Spawn(g.Content.Enemies.Get("zombie"), Feet(g, 10f));
        Run(g, 60, Feet(g, -2f), () => g.Player.State.Buffs.Has("dryads_ward"));
        Assert.True(g.Player.State.Buffs.Has("dryads_ward"));
        Assert.True(dryad.WardTicks > 0);
        Assert.True(dryad.Defense > dryad.Def.Defense);
    }

    [Fact]
    public void ResidentShotsNeverHurtPlayersOrTerrain()
    {
        using var g = Create();
        foreach (var t in g.Content.TownNpcs.All)
        {
            if (t.Attack?.Projectile is not { } id) continue;
            var p = g.Content.Projectiles.Get(id);
            Assert.True(p.Friendly && !p.Hostile, $"{t.Id}: {id} must be friendly");
            foreach (var hit in p.OnHit)
                if (hit.Type == ProjectileHitSpecs.Explode)
                    Assert.False(hit.Parse<ExplodeParams>() is { HurtsPlayers: true } or { DestroyTerrain: true }, $"{t.Id}: {id} blast");
        }
        // A player standing in the line of fire is not hit (the zombie is pinned in place so only arrows could hurt them).
        var guide = Summon(g, "guide", 0f);
        var zombie = g.World.Npcs.Spawn(g.Content.Enemies.Get("zombie"), Feet(g, 12f))!;
        int life = g.Player.State.Vitals.Life;
        for (int i = 0; i < 300 && zombie.Life == zombie.MaxLife; i++)
        {
            g.Player.Position = Feet(g, 5f);
            zombie.Position = Feet(g, 12f);
            zombie.Velocity = Vector3.Zero;
            g.World.Tick();
        }
        Assert.True(guide.Attacks >= 1);
        Assert.True(zombie.Life < zombie.MaxLife);
        Assert.Equal(life, g.Player.State.Vitals.Life);
    }

    [Fact]
    public void FallenResidentsComeBackCalm()
    {
        using var g = Create();
        var guide = Summon(g, "guide", 0f);
        g.World.Npcs.Spawn(g.Content.Enemies.Get("zombie"), Feet(g, 10f));
        Run(g, 200, Feet(g, -30f), () => guide.AttackTicks > 0);
        g.World.Town.Slay(guide);
        Assert.False(guide.Alive);
        Assert.Equal(0, guide.AttackTicks);
        Assert.Null(guide.Threat);
        int attacks = guide.Attacks;
        Run(g, 60, Feet(g, -30f));
        Assert.Equal(attacks, guide.Attacks); // the dead do not shoot
    }
}
