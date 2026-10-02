using System.Numerics;
using Strata.Core.Gameplay.Interaction;
using Strata.Core.Gameplay.Items;
using Strata.Core.Tests.Support;
using Strata.Core.Voxels;

namespace Strata.Core.Tests.Gameplay.Fishing;

/// <summary>A test arena with a pond of one liquid in front of the player, and a cast-wait-reel helper.</summary>
internal static class FishingRig
{
    public static TestGame Pond(LiquidType liquid)
    {
        var g = TestGame.Create();
        g.World.Spawner.Enabled = false;
        var batch = g.World.Session.World.BeginEdit();
        for (int y = -1; y <= 0; y++)
            for (int z = 4; z <= 10; z++)
                for (int x = -3; x <= 3; x++)
                {
                    batch.SetBlock(g.Floor.X + x, g.Floor.Y + y, g.Floor.Z + z, 0);
                    batch.SetLiquid(g.Floor.X + x, g.Floor.Y + y, g.Floor.Z + z, LiquidCode.Full(liquid));
                }
        g.World.Session.Commit(batch);
        g.World.Liquids.Enabled = false;
        g.Player.Aim = Vector3.Normalize(new Vector3(0, -0.35f, 1)); // lob it into the pond 2–5 m ahead
        return g;
    }

    /// <summary>Casts the held rod, waits for the bobber to float, hooks a fish and reels it in.</summary>
    public static List<ItemStack> CastAndReel(TestGame g)
    {
        g.Player.UseCooldown = 0;
        Assert.Equal(UseOutcome.Shot, g.World.ItemUse.Use(g.Player, null, true));
        var bobber = g.World.Projectiles.BobberOf(g.Player)!;
        Assert.NotNull(bobber);
        for (int t = 0; t < 600 && !bobber.Floating; t++) g.World.Tick();
        Assert.True(bobber.Floating, "the bobber never reached the liquid");
        Assert.True(bobber.PoolCells >= 40, $"pool of {bobber.PoolCells} cells");
        bobber.Biting = true;
        return g.World.Projectiles.Reel(bobber);
    }
}
