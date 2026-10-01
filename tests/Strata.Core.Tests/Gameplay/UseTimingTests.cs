using System.Numerics;
using Strata.Core.Commands;
using Strata.Core.Common;
using Strata.Core.Content.Loading;
using Strata.Core.Gameplay.Entities;
using Strata.Core.Gameplay.Interaction;
using Strata.Core.Gameplay.Items;
using Strata.Core.Presentation;
using Strata.Core.Tests.Support;

namespace Strata.Core.Tests.Gameplay;

/// <summary>Use time vs use animation: one clip per swing, speed bonuses, and the strike window of melee hits.</summary>
public class UseTimingTests
{
    private static TestGame Arena()
    {
        var g = TestGame.Create();
        g.World.Spawner.Enabled = false;
        return g;
    }

    [Fact]
    public void ShippedFileValidatesAndMatchesDefaults()
    {
        var shipped = UseStyleConfig.Load(new FileSystemDataSource(TestPaths.ProjectRoot));
        Assert.Empty(shipped.Validate());
        var fallback = UseTiming.Default.Config;
        Assert.Empty(fallback.Validate());
        Assert.Equal(fallback.MaxClipRate, shipped.MaxClipRate);
        Assert.Equal(fallback.Clips.Keys.Order(), shipped.Clips.Keys.Order());
        foreach (var (name, clip) in shipped.Clips)
        {
            var d = fallback.Clips[name];
            Assert.Equal((d.Length, d.StrikeStart, d.StrikeEnd, d.Release, d.RedrawStart, d.RedrawEnd),
                (clip.Length, clip.StrikeStart, clip.StrikeEnd, clip.Release, clip.RedrawStart, clip.RedrawEnd));
        }
    }

    [Fact]
    public void BadTimingIsReported()
    {
        var config = new UseStyleConfig
        {
            MaxClipRate = 9f,
            Clips = new()
            {
                ["swing"] = new ClipTiming { StrikeStart = 0.7f, StrikeEnd = 0.6f },
                ["mine"] = new ClipTiming { StrikeStart = 0.2f },
                ["bow"] = new ClipTiming { Release = 0.5f, RedrawStart = 0.3f, RedrawEnd = 0.8f },
            },
        };
        var problems = config.Validate();
        Assert.Contains(problems, p => p.Contains("maxClipRate"));
        Assert.Contains(problems, p => p.StartsWith("swing:"));
        Assert.Contains(problems, p => p.StartsWith("mine:"));
        Assert.Contains(problems, p => p.Contains("'thrust' is missing"));
        Assert.Contains(problems, p => p.StartsWith("bow:"));
    }

    [Fact]
    public void SweepWaitsOutTheWindUpThenEasesThroughTheStrike()
    {
        var timing = UseTiming.Default;
        var swing = timing.Clip(UseTiming.Swing);
        float start = swing.StrikeStart!.Value, end = swing.StrikeEnd!.Value;
        Assert.Equal(0f, timing.Sweep(UseTiming.Swing, 0f));
        Assert.Equal(0f, timing.Sweep(UseTiming.Swing, start));
        Assert.Equal(0.5f, timing.Sweep(UseTiming.Swing, (start + end) / 2f), 3);
        Assert.Equal(1f, timing.Sweep(UseTiming.Swing, end));
        Assert.Equal(1f, timing.Sweep(UseTiming.Swing, 1f));
        float last = 0f;
        for (int i = 0; i <= 100; i++)
        {
            float s = timing.Sweep(UseTiming.Swing, i / 100f);
            Assert.True(s >= last, "the sweep never goes back");
            last = s;
        }
        var thrust = timing.Clip(UseTiming.Thrust);
        Assert.False(timing.Striking(UseTiming.Thrust, thrust.StrikeStart!.Value - 0.01f));
        Assert.True(timing.Striking(UseTiming.Thrust, thrust.StrikeStart!.Value));
        Assert.True(timing.Striking(UseTiming.Thrust, thrust.StrikeEnd!.Value + UseTiming.FollowThrough));
        Assert.False(timing.Striking(UseTiming.Thrust, thrust.StrikeEnd!.Value + UseTiming.FollowThrough + 0.01f));
    }

    /// <summary>A stone wall two cells ahead, thick enough that a pickaxe never digs through it in a few seconds.</summary>
    private static void StoneWall(TestGame g)
    {
        var stone = g.Content.BlockId("stone");
        var batch = g.World.Session.World.BeginEdit();
        for (int z = 2; z <= TestGame.ArenaRadius; z++)
            for (int y = 1; y <= 8; y++)
                for (int x = -4; x <= 4; x++)
                    batch.SetBlock(g.Floor.X + x, g.Floor.Y + y, g.Floor.Z + z, stone);
        g.World.Session.Commit(batch);
    }

    /// <summary>Holds the use button on the wall for <paramref name="ticks"/>: (animations started, digs, outcomes).</summary>
    private static (int Swings, int Digs, List<UseOutcome> Used) HoldOnWall(TestGame g, int ticks)
    {
        var used = new List<UseOutcome>();
        g.World.ItemUse.Used += (_, _, o) => used.Add(o);
        int serial = g.Player.UseSerial;
        for (int tick = 0; tick < ticks; tick++)
        {
            int animationLeft = g.Player.UseAnimation;
            int before = g.Player.UseSerial;
            g.World.ItemUse.Use(g.Player, g.Target(), tick == 0);
            if (g.Player.UseSerial != before)
                Assert.True(animationLeft == 0, $"tick {tick}: a new swing started with {animationLeft} ticks of the last one left");
            g.World.Tick();
        }
        int digs = used.Count(o => o is UseOutcome.Mined or UseOutcome.Damaged);
        return (g.Player.UseSerial - serial, digs, used);
    }

    [Fact]
    public void ToolSwingsNeverRestartMidAnimation()
    {
        using var g = Arena();
        StoneWall(g);
        g.Hold("copper_pickaxe"); // use time 15, animation 21
        g.AimAt(g.Player.Eye + Vector3.UnitZ);
        var (swings, digs, used) = HoldOnWall(g, 84);
        Assert.Equal(4, swings);                 // 84 / 21: the swing plays out every time
        Assert.Equal(6, digs);                   // ticks 0, 15, 30, 45, 60, 75: the dig keeps its own cadence
        Assert.True(used.Count >= 5);
        Assert.Equal(21, g.Player.UseAnimationMax);
    }

    [Fact]
    public void MiningSpeedAddsHitsButNeverShortensOrRestartsTheSwing()
    {
        using var g = Arena();
        StoneWall(g);
        g.Player.State.Buffs.Add(g.Content.Buffs.Get("mining"), 6000); // +25% mining speed
        g.Player.State.Recompute();
        g.Hold("copper_pickaxe");
        g.AimAt(g.Player.Eye + Vector3.UnitZ);
        var (swings, digs, _) = HoldOnWall(g, 84);
        Assert.Equal(4, swings);
        Assert.Equal(21, g.Player.UseAnimationMax); // the animation is untouched
        Assert.Equal(7, digs);                      // every 12 ticks instead of 15
    }

    [Fact]
    public void MeleeSpeedScalesTheAnimation()
    {
        using var g = Arena();
        var state = g.Player.State;
        state.Inventory.Accessories[0] = new ItemStack(g.Content.Items.Get("feral_claws"), 1);
        state.Recompute();
        var sword = g.Content.Items.Get("wooden_sword");
        g.Hold(sword.Id);
        g.Player.Aim = Vector3.UnitZ;
        Assert.Equal(UseOutcome.Swung, g.World.ItemUse.Use(g.Player, null, true));
        Assert.Equal((int)MathF.Round(sword.UseAnimation / 1.12f), g.Player.UseAnimationMax);
        Assert.Equal(g.Player.UseAnimationMax, g.Player.UseAnimation);
        Assert.Equal((int)MathF.Ceiling(sword.UseTime / 1.12f), g.Player.UseCooldown);
        Assert.Equal(1, g.Player.UseSerial);
    }

    [Fact]
    public void PrefixSpeedScalesTheAnimation()
    {
        using var g = Arena();
        var sword = g.Content.Items.Get("wooden_sword");
        g.Player.State.Inventory.Backpack[0] = new ItemStack(sword, 1, g.Content.Prefixes.Get("brisk"));
        g.Player.State.Inventory.Select(0);
        g.Player.Aim = Vector3.UnitZ;
        g.World.ItemUse.Use(g.Player, null, true);
        Assert.Equal((int)MathF.Round(sword.UseAnimation / 1.1f), g.Player.UseAnimationMax);
        Assert.Equal((int)MathF.Ceiling(sword.UseTime / 1.1f), g.Player.UseCooldown);
    }

    [Fact]
    public void OneStrikePerSwing()
    {
        using var g = Arena();
        g.Hold("wooden_sword");
        g.Player.Aim = Vector3.UnitZ;
        Assert.Equal(UseOutcome.Swung, g.World.ItemUse.Use(g.Player, null, true));
        g.Ticks(3);
        g.Player.UseCooldown = 0; // only the animation is still running
        Assert.Equal(UseOutcome.Cooldown, g.World.ItemUse.Use(g.Player, null, true));
        Assert.Equal(1, g.World.Melee.ActiveSwings);
        Assert.Equal(1, g.Player.UseSerial);
        g.Player.ResetUse();
        Assert.Equal(UseOutcome.Swung, g.World.ItemUse.Use(g.Player, null, true));
        Assert.Equal(2, g.Player.UseSerial);
    }

    [Fact]
    public void TheUseCommandStartsAFreshSwingAndTheSwingProbeCountsFromTheMark()
    {
        using var g = Arena();
        var ctx = new CommandContext(g.World, new HeadlessCommandHost()) { Registry = BuiltinCommands.CreateRegistry() };
        var r = ctx.Registry!;
        g.Hold("wooden_sword");
        g.Player.Aim = Vector3.UnitZ;
        Assert.True(r.Execute(ctx, "use").Ok);
        Assert.True(r.Execute(ctx, "use").Ok); // mid-swing, yet a scripted use starts a new one
        Assert.Equal(2, ctx.ReadProbe("player.swings"));
        Assert.Equal(g.Content.Items.Get("wooden_sword").UseAnimation, ctx.ReadProbe("player.use_anim"));
        Assert.True(r.Execute(ctx, "mark").Ok);
        Assert.Equal(0, ctx.ReadProbe("player.swings"));
        Assert.True(r.Execute(ctx, "use").Ok);
        Assert.Equal(1, ctx.ReadProbe("player.swings"));
    }

    private static Npc Dummy(TestGame g, float yawDegrees, float meters)
    {
        float yaw = yawDegrees * MathF.PI / 180f;
        var at = g.Player.Position + new Vector3(MathF.Sin(yaw) * meters, 0.05f, MathF.Cos(yaw) * meters);
        var npc = g.World.Npcs.Spawn(g.Content.Enemies.Get("green_slime"), at)!;
        npc.Life = npc.MaxLife = 5000;
        npc.Timer = -100_000; // sit still
        return npc;
    }

    [Fact]
    public void SwingHitsOnlyAfterTheWindUp()
    {
        using var g = Arena();
        // The 150° arc sweeps from +75° (the start side) to −75° (the end side) around the facing.
        var first = Dummy(g, 65f, 1.4f);
        var last = Dummy(g, -65f, 1.4f);
        g.Hold("wooden_sword");
        g.Player.Aim = Vector3.UnitZ;
        Assert.Equal(UseOutcome.Swung, g.World.ItemUse.Use(g.Player, null, true));
        int duration = g.Player.UseAnimationMax;
        float windUp = g.World.ItemUse.Timing.StrikeStart(UseTiming.Swing) * duration;
        int firstHit = -1, lastHit = -1;
        for (int tick = 1; tick <= duration; tick++)
        {
            g.World.Tick();
            if (firstHit < 0 && first.Life < 5000) firstHit = tick;
            if (lastHit < 0 && last.Life < 5000) lastHit = tick;
        }
        Assert.True(firstHit >= windUp, $"the start side was hit at tick {firstHit}, inside the {windUp:0.0}-tick wind-up");
        Assert.True(lastHit >= windUp, $"the end side was hit at tick {lastHit}, inside the wind-up");
        Assert.True(firstHit > 0 && lastHit > 0, "the swing missed a slime inside its arc");
        Assert.True(lastHit > firstHit, "the blade reached the end side before the start side");
    }

    [Fact]
    public void ThrustsConnectOnlyInTheStrikeWindow()
    {
        using var g = Arena();
        var gel = Dummy(g, 0f, 1.0f);
        g.Hold("copper_shortsword");
        g.Player.Aim = Vector3.UnitZ;
        Assert.Equal(UseOutcome.Swung, g.World.ItemUse.Use(g.Player, null, true));
        int duration = g.Player.UseAnimationMax;
        int hit = -1;
        for (int tick = 1; tick <= duration && hit < 0; tick++)
        {
            g.World.Tick();
            if (gel.Life < 5000) hit = tick;
        }
        Assert.True(hit > 0, "the thrust missed");
        Assert.True(g.World.ItemUse.Timing.Striking(UseTiming.Thrust, hit / (float)duration), $"hit at tick {hit} of {duration}, outside the strike");
    }
}
