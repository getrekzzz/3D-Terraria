using System.Numerics;
using Strata.Core.Common;
using Strata.Core.Gameplay.Entities;
using Strata.Core.Gameplay.Interaction;
using Strata.Core.Gameplay.Items;
using Strata.Core.Tests.Support;
using Strata.Core.Voxels;

namespace Strata.Core.Tests.Gameplay;

public class ArsenalTests
{
    private static TestGame Arena()
    {
        var g = TestGame.Create();
        g.World.Spawner.Enabled = false;
        return g;
    }

    private static Npc Dummy(TestGame g, float meters, string enemy = "green_slime")
    {
        var npc = g.World.Npcs.Spawn(g.Content.Enemies.Get(enemy), g.Player.Position + new Vector3(0, 0.05f, meters))!;
        npc.Life = npc.MaxLife = 5000;
        npc.Timer = -100_000; // sit still
        return npc;
    }

    [Fact]
    public void BoomerangsHitAndComeBackOneAtATime()
    {
        using var g = Arena();
        var gel = Dummy(g, 5f);
        g.Hold("wooden_boomerang");
        g.Player.Aim = Vector3.Normalize(gel.Center - g.Player.Eye);
        Assert.Equal(UseOutcome.Shot, g.World.ItemUse.Use(g.Player, null, true));
        g.Player.UseCooldown = 0;
        Assert.Equal(UseOutcome.Blocked, g.World.ItemUse.Use(g.Player, null, true)); // still out
        g.Ticks(30);
        Assert.True(gel.Life < 5000, "boomerang missed");
        g.Ticks(120);
        Assert.Empty(g.World.Projectiles.All); // caught again
        Assert.Equal(1, g.Player.State.Inventory.Held.Count); // not consumed
    }

    [Theory]
    [InlineData("starfury")]
    [InlineData("enchanted_sword")]
    public void SwordsWithProjectilesAlsoSwing(string sword)
    {
        using var g = Arena();
        var gel = Dummy(g, 1.5f);
        g.Hold(sword);
        // Pitched 35° up, so the shot sails over the slime and only the blade can reach it.
        float pitch = 35f * MathF.PI / 180f;
        g.Player.Aim = new Vector3(0, MathF.Sin(pitch), MathF.Cos(pitch));
        Assert.Equal(UseOutcome.Shot, g.World.ItemUse.Use(g.Player, null, true));
        Assert.Equal(1, g.World.Melee.ActiveSwings);
        Assert.NotEmpty(g.World.Projectiles.All);
        bool bladeHit = false;
        for (int t = 0; t < 30; t++)
        {
            g.World.Tick();
            bladeHit |= gel.ImmuneTicks > 0; // only melee hits grant immunity frames
        }
        Assert.True(gel.Life < 5000, "the slime was never hit");
        Assert.True(bladeHit, "the blade never connected");
    }

    [Fact]
    public void ZenithNeverBlocks()
    {
        using var g = Arena();
        g.Hold("zenith");
        g.Player.Aim = Vector3.UnitZ;
        Assert.Equal(UseOutcome.Shot, g.World.ItemUse.Use(g.Player, null, true));
        g.Player.UseCooldown = g.Player.UseAnimation = 0; // a fresh use while the first blades are still out
        Assert.Equal(UseOutcome.Shot, g.World.ItemUse.Use(g.Player, null, true));
        Assert.Equal(6, g.World.Projectiles.All.Count);
        Assert.Equal(0, g.World.Melee.ActiveSwings); // the swing is only an animation
    }

    [Fact]
    public void BombsExplodeOnTheirFuseDiggingAndHurting()
    {
        using var g = Arena();
        var gel = Dummy(g, 2.5f);
        g.Hold("bomb", 3);
        g.Player.Aim = Vector3.Normalize(new Vector3(0, -0.2f, 1));
        int life = g.Player.State.Vitals.Life;
        int stone = CountSolid(g, 8);
        Assert.Equal(UseOutcome.Shot, g.World.ItemUse.Use(g.Player, null, true));
        Assert.Equal(2, g.Player.State.Inventory.Held.Count); // thrown bombs are used up
        g.Ticks(60);
        Assert.Single(g.World.Projectiles.All); // still fizzing: bombs do not burst on contact
        Assert.Equal(5000, gel.Life);
        g.Ticks(150);
        Assert.Empty(g.World.Projectiles.All);
        Assert.True(gel.Life < 5000, "blast missed the slime");
        Assert.True(CountSolid(g, 8) < stone, "blast dug nothing");
        Assert.True(g.Player.State.Vitals.Life <= life); // a close blast may hurt the thrower too
    }

    private static int CountSolid(TestGame g, int radius)
    {
        int n = 0;
        var w = g.World.Session.World;
        for (int y = -1; y <= 0; y++)
            for (int z = -radius; z <= radius; z++)
                for (int x = -radius; x <= radius; x++)
                    if (w.IsSolid(g.Floor.X + x, g.Floor.Y + y, g.Floor.Z + z)) n++;
        return n;
    }

    [Fact]
    public void MinionsFollowAttackAndRespectTheSlotLimit()
    {
        using var g = Arena();
        g.Hold("imp_staff");
        g.Player.State.Vitals.RestoreMana(200);
        Assert.Equal(UseOutcome.Shot, g.World.ItemUse.Use(g.Player, null, true));
        g.Player.UseCooldown = 0;
        g.Ticks(40);
        g.World.ItemUse.Use(g.Player, null, true);
        Assert.Single(g.World.Projectiles.MinionsOf(g.Player)); // one slot: the first imp was unsummoned
        g.Ticks(120);
        var imp = g.World.Projectiles.MinionsOf(g.Player).Single();
        Assert.True(Vector3.Distance(imp.Position, g.Player.Center) < 4f, "the imp wandered off");
        var gel = Dummy(g, 6f);
        g.Ticks(300);
        Assert.True(gel.Life < 5000, "the imp never attacked");
        g.Player.State.Vitals.Life = 0;
        g.Ticks(2);
        Assert.Empty(g.World.Projectiles.MinionsOf(g.Player)); // minions vanish with their summoner
    }

    [Fact]
    public void MagicMirrorSendsThePlayerHome()
    {
        using var g = Arena();
        var home = g.World.SpawnPositionFor(g.Player.State);
        g.Player.Position += new Vector3(4, 0, 4);
        Vector3? recalled = null;
        g.World.ItemUse.Recalled += (_, at) => recalled = at;
        g.Hold("magic_mirror");
        Assert.Equal(UseOutcome.Consumed, g.World.ItemUse.Use(g.Player, null, true));
        Assert.Equal(home, recalled);
        Assert.Equal(home, g.Player.Position);
        Assert.Equal(1, g.Player.State.Inventory.Held.Count); // the mirror is reusable
    }

    [Fact]
    public void FishingLandsACatchWhenReeledDuringABite()
    {
        using var g = Arena();
        // A pond in front of the player.
        var batch = g.World.Session.World.BeginEdit();
        for (int z = 4; z <= 10; z++)
            for (int x = -3; x <= 3; x++)
            {
                batch.SetBlock(g.Floor.X + x, g.Floor.Y, g.Floor.Z + z, 0);
                batch.SetLiquid(g.Floor.X + x, g.Floor.Y, g.Floor.Z + z, LiquidCode.Full(LiquidType.Water));
            }
        g.World.Session.Commit(batch);
        g.World.Liquids.Enabled = false;
        g.Hold("golden_fishing_rod");
        g.Player.Aim = Vector3.Normalize(new Vector3(0, -0.35f, 1)); // lob it into the pond 2–5 m ahead
        int caught = 0;
        for (int cast = 0; cast < 6 && caught == 0; cast++)
        {
            Assert.Equal(UseOutcome.Shot, g.World.ItemUse.Use(g.Player, null, true));
            var bobber = g.World.Projectiles.BobberOf(g.Player)!;
            Assert.NotNull(bobber);
            for (int t = 0; t < 1200 && !bobber.Biting; t++) g.World.Tick();
            Assert.True(bobber.Floating, "the bobber never reached the water");
            Assert.True(bobber.Biting, "no bite");
            g.Player.UseCooldown = 0;
            int before = Filled(g);
            g.World.ItemUse.Use(g.Player, null, true);
            Assert.Null(g.World.Projectiles.BobberOf(g.Player));
            caught = Filled(g) - before;
            g.Player.UseCooldown = 0;
        }
        Assert.True(caught > 0, "six bites and nothing landed");
    }

    private static int Filled(TestGame g)
    {
        int n = 0;
        var b = g.Player.State.Inventory.Backpack;
        for (int i = 0; i < b.Size; i++)
            if (!b[i].IsEmpty) n++;
        return n;
    }

    [Fact]
    public void CratesOpenIntoTheBackpack()
    {
        using var g = Arena();
        g.Hold("golden_crate", 2);
        long before = Coins.Total(g.Player.State.Inventory.Backpack);
        Assert.Equal(UseOutcome.Consumed, g.World.ItemUse.Use(g.Player, null, true));
        Assert.Equal(1, g.Player.State.Inventory.Held.Count);
        Assert.True(Coins.Total(g.Player.State.Inventory.Backpack) > before, "a golden crate always holds coins");
    }
}
