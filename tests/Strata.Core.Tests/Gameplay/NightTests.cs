using System.Numerics;
using Strata.Core.Common;
using Strata.Core.Gameplay.Entities;
using Strata.Core.Gameplay.Items;
using Strata.Core.Simulation;
using Strata.Core.Tests.Support;
using Strata.Core.World;

namespace Strata.Core.Tests.Gameplay;

/// <summary>The first nights for a new character: reference-game spawn rules plus safe ground.</summary>
public class NightTests
{
    private static TestGame Night(Difficulty difficulty = Difficulty.Classic)
    {
        var g = TestGame.Create(difficulty: difficulty);
        g.World.Spawner.Enabled = false;
        g.World.Session.Clock.SetHour(23f);
        return g;
    }

    private static List<Npc> TrySpawns(TestGame g, int tries = 40)
    {
        var spawned = new List<Npc>();
        for (int i = 0; i < tries; i++) spawned.AddRange(g.World.Spawner.TrySpawnNear(g.Player));
        return spawned;
    }

    private static bool Hostile(Npc n) => !n.Def.HasTag("critter");

    [Fact]
    public void NoHostileAppearsNearTheWorldSpawn()
    {
        using var g = Night();
        g.World.Spawner.Config = new SpawnConfig { SafeRadius = 60 }; // wider than the spawn ring: all of it is safe
        Assert.DoesNotContain(TrySpawns(g), Hostile);

        g.World.Spawner.Config = new SpawnConfig { SafeRadius = 0 };
        Assert.Contains(TrySpawns(g), Hostile);
    }

    [Fact]
    public void PlacedLightKeepsTheSurfaceSafeExceptDuringEvents()
    {
        using var g = Night();
        g.World.Spawner.Config = new SpawnConfig { SafeRadius = 0, SafeLight = 0 }; // every surface cell counts as lit
        Assert.DoesNotContain(TrySpawns(g), Hostile);

        Assert.True(g.World.Events.Start(g.Content.Events.Get("blood_moon")));
        Assert.Contains(TrySpawns(g), Hostile);
    }

    [Fact]
    public void CrittersDoNotFillTheHostileAllowance()
    {
        using var g = Night();
        var firefly = g.Content.Enemies.Get("firefly");
        for (int i = 0; i < 4; i++) g.World.Npcs.Spawn(firefly, g.Player.Position + new Vector3(i, 1f, 3f));
        Assert.Equal(0, g.World.Npcs.CountNear(g.Player.Center, 60f, critters: false));
        Assert.Equal(4, g.World.Npcs.CountNear(g.Player.Center, 60f, critters: true));
    }

    [Fact]
    public void EveryResidentNearbySlowsSpawning()
    {
        using var g = Night();
        Assert.Equal(1f, g.World.Spawner.TownFactor(g.Player));
        var at = g.World.Navigator.NearestStandable(g.Floor + new Int3(0, 1, -6), Strata.Core.Gameplay.Navigation.NavAgent.Humanoid, 3)!.Value;
        g.World.Town.Summon(g.Content.TownNpcs.Get("guide"), at); // homeless: no house built
        float withGuide = g.World.Spawner.TownFactor(g.Player);
        Assert.True(withGuide < 1f, $"a homeless Guide should slow spawning ({withGuide})");
        g.Player.Position += new Vector3(0, 0, 500f);
        Assert.Equal(1f, g.World.Spawner.TownFactor(g.Player));
    }

    [Fact]
    public void BloodMoonNeedsMoreThanStartingLife()
    {
        using var g = TestGame.Create();
        g.World.Spawner.Enabled = false;
        int started = 0;
        g.World.Events.Started += e => { if (e.Id == "blood_moon") started++; };
        var clock = g.World.Session.Clock;
        for (int night = 0; night < 60; night++)
        {
            clock.SetHour(GameClock.DuskHour - 0.01f);
            g.Ticks(2);
            clock.SetHour(GameClock.DuskHour + 0.01f);
            g.Ticks(2);
            clock.SetHour(GameClock.DawnHour + 0.01f);
            g.Ticks(2);
            clock.Advance(GameClock.TicksPerDay);
        }
        Assert.Equal(0, started);
    }

    [Fact]
    public void DemonEyesLeaveAtDawn()
    {
        using var g = Night();
        var eye = g.World.Npcs.Spawn(g.Content.Enemies.Get("demon_eye"), g.Player.Position + new Vector3(0, 2f, 6f))!;
        g.Ticks(60);
        Assert.Equal(0, eye.TargetPlayer);
        g.World.Session.Clock.SetHour(9f);
        float before = Vector3.Distance(eye.Center, g.Player.Center);
        g.Ticks(60 * 4);
        Assert.Equal(-1, eye.TargetPlayer);
        Assert.True(Vector3.Distance(eye.Center, g.Player.Center) > before + 5f, "the eye should fly away by day");
    }

    [Fact]
    public void ZombiesWalkAwaySteadilyAtDawnAndDespawnOutOfSight()
    {
        using var g = Night();
        var zombie = g.World.Npcs.Spawn(g.Content.Enemies.Get("zombie"), g.Player.Position + new Vector3(0, 0.05f, 4f))!;
        g.Ticks(30);
        g.World.Session.Clock.SetHour(9f);
        g.Ticks(1);
        Assert.True(zombie.Leaving);
        var start = zombie.Position;
        var heading = zombie.Direction;
        int reversals = 0;
        for (int i = 0; i < 60 * 3; i++)
        {
            g.Ticks(1);
            if (Vector3.Dot(heading, zombie.Direction) < -0.5f) reversals++; // turning aside is fine, flipping back is the old jitter
            heading = zombie.Direction;
        }
        Assert.Equal(0, reversals);
        Assert.Equal(-1, zombie.TargetPlayer);
        Assert.True(Vector3.Distance(start, zombie.Position) > 2.5f, "the zombie should walk off");
        g.Player.Position -= Vector3.Normalize(zombie.Position - g.Player.Position) * 40f; // out of sight
        g.Ticks(2);
        Assert.DoesNotContain(zombie, g.World.Npcs.All);
    }

    [Fact]
    public void HitsMakeCreaturesReel()
    {
        using var g = Night();
        var zombie = g.World.Npcs.Spawn(g.Content.Enemies.Get("zombie"), g.Player.Position + new Vector3(0, 0.05f, 2f))!;
        g.Ticks(20);
        Assert.True(g.World.Npcs.Hit(zombie, g.Player, 5, Strata.Core.Content.Defs.DamageClass.Melee, 0, 4f, g.Player.Center, melee: true));
        Assert.True(zombie.StunTicks > 0);
        float start = zombie.Position.Z;
        g.Ticks(zombie.StunTicks);
        Assert.True(zombie.Position.Z > start + 0.3f, $"knockback should carry the zombie away ({zombie.Position.Z - start:0.00} m)");
    }

    [Fact]
    public void RespawningGivesImmunityAndClearsTheSpawnPoint()
    {
        using var g = Night();
        var spawn = g.World.SpawnPositionFor(g.Player.State);
        var zombie = g.World.Npcs.Spawn(g.Content.Enemies.Get("zombie"), spawn + new Vector3(3f, 0.05f, 0))!;
        g.World.Respawn(g.Player);
        Assert.DoesNotContain(zombie, g.World.Npcs.All);
        Assert.Equal(Strata.Core.Session.GameWorld.RespawnImmunityTicks, g.Player.State.Vitals.ImmunityTicks);
    }

    [Fact]
    public void ExpertAndMasterScaleCreatures()
    {
        using var classic = Night();
        using var expert = Night(Difficulty.Expert);
        using var master = Night(Difficulty.Master);
        Npc Zombie(TestGame g) => g.World.Npcs.Spawn(g.Content.Enemies.Get("zombie"), g.Player.Position + new Vector3(0, 0.05f, 8f))!;
        Assert.Equal(45, Zombie(classic).MaxLife);
        Assert.Equal(90, Zombie(expert).MaxLife);
        Assert.Equal(135, Zombie(master).MaxLife);
    }

    [Fact]
    public void CreaturesPayCoinsOnce()
    {
        using var g = Night();
        var table = g.Content.LootTables.Get("zombie");
        for (int i = 0; i < 20; i++)
        {
            int before = g.World.Drops.Items.Count;
            var zombie = g.World.Npcs.Spawn(g.Content.Enemies.Get("zombie"), g.Player.Position + new Vector3(0, 0.05f, 8f))!;
            g.World.Npcs.Kill(zombie);
            long copper = g.World.Drops.Items.Skip(before).Where(d => d.Stack.Item!.HasTag("coin")).Sum(d => (long)d.Stack.Item!.Value * d.Stack.Count);
            Assert.InRange(copper, table.Coins![0], table.Coins[1]);
        }
    }
}
