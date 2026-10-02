using System.Numerics;
using Strata.Core.Gameplay.Interaction;
using Strata.Core.Tests.Support;

namespace Strata.Core.Tests.Gameplay;

/// <summary>Flails on a chain (P3-13): whirling at the hand while held, flung along the aim when let go, then reeled in.</summary>
public class FlailTests
{
    private static TestGame Ready()
    {
        var g = TestGame.Create();
        g.World.Spawner.Enabled = false;
        g.Hold("ball_o_hurt");
        g.Player.Aim = Vector3.UnitZ;
        g.Player.UseHeld = true;
        return g;
    }

    [Fact]
    public void HeldItWhirlsAndStrikesWhatIsNear()
    {
        using var g = Ready();
        var gel = g.World.Npcs.Spawn(g.Content.Enemies.Get("green_slime"), g.Player.Position + new Vector3(0, 0.05f, 1f))!;
        gel.Life = gel.MaxLife = 5000;
        gel.Timer = -100_000;
        Assert.Equal(UseOutcome.Shot, g.World.ItemUse.Use(g.Player, null, true));
        var ball = g.World.Projectiles.TetherOf(g.Player)!;
        int hits = 0, last = gel.Life;
        var pinned = gel.Position;
        for (int t = 0; t < 120; t++)
        {
            gel.Position = pinned; // a training dummy: knockback does not carry it out of reach
            gel.Velocity = Vector3.Zero;
            g.World.Tick();
            if (gel.Life < last) hits++;
            last = gel.Life;
            Assert.False(ball.Launched);
            Assert.True(Vector3.Distance(ball.Position, g.Player.Eye) < 2f, "the ball left the hand while held");
        }
        Assert.True(hits >= 3, $"only {hits} strikes in 2 s of whirling");
        g.Player.ResetUse();
        Assert.NotEqual(UseOutcome.Shot, g.World.ItemUse.Use(g.Player, null, true)); // one flail at a time
    }

    [Fact]
    public void LetGoItFliesOutAlongTheAimAndComesBack()
    {
        using var g = Ready();
        // Clear air ahead and a little up, so the throw is not cut short by the ground.
        g.Player.Aim = Vector3.Normalize(new Vector3(0, 0.25f, 1));
        Assert.Equal(UseOutcome.Shot, g.World.ItemUse.Use(g.Player, null, true));
        var ball = g.World.Projectiles.TetherOf(g.Player)!;
        g.Ticks(20);
        g.Player.UseHeld = false;
        var start = g.Player.Eye;
        float farthest = 0;
        int t = 0;
        for (; t < 180 && g.World.Projectiles.All.Contains(ball); t++)
        {
            g.World.Tick();
            if (ball.Launched && !ball.Returning) farthest = MathF.Max(farthest, Vector3.Dot(ball.Position - start, g.Player.Aim));
        }
        Assert.True(farthest >= 4f, $"the ball only went {farthest:0.0} m");
        Assert.DoesNotContain(ball, g.World.Projectiles.All);
    }
}
