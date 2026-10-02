using System.Numerics;
using Strata.Core.Common;
using Strata.Core.Gameplay.Entities;
using Strata.Core.Gameplay.Items;
using Strata.Core.Tests.Support;

namespace Strata.Core.Tests.Gameplay;

public class CombatTests
{
    private static TestGame Arena()
    {
        var g = TestGame.Create();
        g.World.Spawner.Enabled = false;
        return g;
    }

    private static Npc SpawnInFront(TestGame g, string enemy, float meters)
    {
        var def = g.Content.Enemies.Get(enemy);
        return g.World.Npcs.Spawn(def, g.Player.Position + new Vector3(0, 0.05f, meters))!;
    }

    [Fact]
    public void GelsHopTowardsThePlayerAndDealContactDamage()
    {
        using var g = Arena();
        var gel = SpawnInFront(g, "green_slime", 6f);
        float start = Vector3.Distance(gel.Position, g.Player.Position);
        int life = g.Player.State.Vitals.Life;
        g.Ticks(60 * 8);
        Assert.True(g.Player.State.Vitals.Life < life, "gel never damaged the player");
        Assert.True(Vector3.Distance(gel.Position, g.Player.Position) < start);
    }

    [Fact]
    public void ImmunityFramesLimitContactDamage()
    {
        using var g = Arena();
        var hurt = new List<int>();
        g.World.Npcs.PlayerHurt += (_, dmg, _) => hurt.Add(dmg);
        SpawnInFront(g, "green_slime", 0.2f);
        g.Ticks(NpcSystem.PlayerHitImmunity * 2 + 5);
        Assert.InRange(hurt.Count, 1, 3);
    }

    [Fact]
    public void SwordSwingsHitOncePerSwingAndKillsDropLoot()
    {
        using var g = Arena();
        var gel = SpawnInFront(g, "green_slime", 1.2f);
        gel.Life = gel.MaxLife = 1000;
        g.Hold("wooden_sword");
        g.Player.Aim = Vector3.UnitZ;
        var damage = new List<DamageEvent>();
        g.World.Npcs.Damaged += e => { if (!e.ToPlayer) damage.Add(e); };
        g.World.ItemUse.Use(g.Player, null, true);
        g.Ticks(40);
        Assert.Single(damage);
        Assert.True(gel.Life < 1000);

        gel.Life = 1;
        g.Ticks(30);
        // The first blow knocked it back out of reach: bring it in front again.
        gel.Position = g.Player.Position + new Vector3(0, 0.05f, 1.2f);
        gel.Velocity = Vector3.Zero;
        g.World.ItemUse.Use(g.Player, null, true);
        g.Ticks(30);
        Assert.False(gel.Alive);
        Assert.DoesNotContain(gel, g.World.Npcs.All);
        // Dying this close, its gel may already be in the pack.
        Assert.True(g.World.Drops.Items.Any(d => d.Stack.Item!.Id == "gel") || g.Player.State.Inventory.Backpack.Count(i => i.Id == "gel") > 0);
    }

    [Fact]
    public void ShortswordThrustsHitAndRepeatWhileHeld()
    {
        using var g = Arena();
        var gel = SpawnInFront(g, "green_slime", 1.0f);
        gel.Life = gel.MaxLife = 1000;
        gel.Timer = -100_000; // sit still
        g.Hold("copper_shortsword");
        g.Player.Aim = Vector3.UnitZ;
        Assert.Equal(Strata.Core.Gameplay.Interaction.UseOutcome.Swung, g.World.ItemUse.Use(g.Player, null, true));
        g.Ticks(20);
        Assert.True(gel.Life < 1000);
        // Held down (not a fresh press) it stabs again once the use time has passed.
        gel.Position = g.Player.Position + new Vector3(0, 0.05f, 1.0f); // back in reach after the knockback
        gel.Velocity = Vector3.Zero;
        int life = gel.Life;
        Assert.Equal(Strata.Core.Gameplay.Interaction.UseOutcome.Swung, g.World.ItemUse.Use(g.Player, null, false));
        g.Ticks(20);
        Assert.True(gel.Life < life);
    }

    [Fact]
    public void SwingsMissCreaturesBehindThePlayer()
    {
        using var g = Arena();
        var gel = SpawnInFront(g, "green_slime", -1.5f);
        g.Hold("wooden_sword");
        g.Player.Aim = Vector3.UnitZ;
        g.World.ItemUse.Use(g.Player, null, true);
        g.Ticks(30);
        Assert.Equal(gel.MaxLife, gel.Life);
    }

    [Fact]
    public void ArrowsFlyAndHitCreatures()
    {
        using var g = Arena();
        var gel = SpawnInFront(g, "green_slime", 8f);
        gel.Life = gel.MaxLife = 500;
        gel.Timer = -100_000; // sit still
        g.Hold("wooden_bow");
        g.Player.State.Inventory.Backpack[30] = new ItemStack(g.Content.Items.Get("wooden_arrow"), 5);
        // Aim above the target to allow for the arrow's drop over 8 m.
        g.Player.Aim = Vector3.Normalize(gel.Center + new Vector3(0, 1.3f, 0) - g.Player.Eye);
        Assert.Equal(Strata.Core.Gameplay.Interaction.UseOutcome.Shot, g.World.ItemUse.Use(g.Player, null, true));
        Assert.Single(g.World.Projectiles.All);
        g.Ticks(90);
        Assert.True(gel.Life < 500, "arrow missed");
        Assert.Empty(g.World.Projectiles.All);
    }

    [Fact]
    public void WalkersClimbOneBlockLedges()
    {
        using var g = Arena();
        var batch = g.World.Session.World.BeginEdit();
        for (int x = -3; x <= 3; x++) batch.SetBlock(g.Floor + new Int3(x, 1, 5), g.Content.BlockId("stone"));
        g.World.Session.Commit(batch);
        var walker = SpawnInFront(g, "zombie", 5f);
        walker.Position += new Vector3(0, 0, 2.5f); // behind the ledge
        g.World.Session.Clock.SetHour(23f);
        g.Ticks(60 * 6);
        Assert.True(walker.Position.Z - g.Player.Position.Z < 2.4f, $"walker stuck at z offset {walker.Position.Z - g.Player.Position.Z:0.00}");
    }

    [Fact]
    public void SpawnDirectorPicksRulesMatchingTheLocation()
    {
        using var g = Arena();
        g.World.Spawner.Config = new SpawnConfig { SafeRadius = 0 }; // the arena is the world spawn
        var spawned = new List<Npc>();
        for (int i = 0; i < 40 && !spawned.Any(Hostile); i++) spawned.AddRange(g.World.Spawner.TrySpawnNear(g.Player));
        Assert.Contains(spawned, Hostile);
        Assert.All(spawned.Where(Hostile), n => Assert.Contains(n.Def.Id, new[] { "green_slime", "blue_slime", "pinky" })); // surface, daytime
        g.World.Npcs.Clear();
        g.World.Session.Clock.SetHour(23f);
        spawned.Clear();
        for (int i = 0; i < 40 && !spawned.Any(Hostile); i++) spawned.AddRange(g.World.Spawner.TrySpawnNear(g.Player));
        Assert.Contains(spawned, Hostile);
        // Ravens (Pumpkin Moon) and the Groom (Blood Moon) no longer join ordinary nights.
        Assert.All(spawned.Where(Hostile), n => Assert.Contains(n.Def.Id, new[] { "zombie", "bald_zombie", "demon_eye", "blue_slime" }));
    }

    private static bool Hostile(Npc n) => !n.Def.HasTag("critter");

    [Fact]
    public void CreaturesDespawnFarFromPlayers()
    {
        using var g = Arena();
        var gel = SpawnInFront(g, "green_slime", 3f);
        g.Player.Position += new Vector3(0, 0, 200f);
        g.Ticks(NpcSystem.DespawnDelayTicks + 5);
        Assert.DoesNotContain(gel, g.World.Npcs.All);
    }
}
