using System.Numerics;
using Strata.Core.Gameplay.Entities;
using Strata.Core.Gameplay.Interaction;
using Strata.Core.Gameplay.Items;
using Strata.Core.Presentation;
using Strata.Core.Tests.Support;

namespace Strata.Core.Tests.Gameplay;

/// <summary>The shared swing curve: the blade angle and thrust extension over a use, and the hits sampled from it.</summary>
public class SwingCurveTests
{
    private static readonly ClipTiming SwingClip = UseStyleConfig.Fallback().Clips["swing"];
    private static readonly ClipTiming ThrustClip = UseStyleConfig.Fallback().Clips["spear"]; // the spear's lunge (a rapier jabs on its own, shorter window);

    [Fact]
    public void TheBladeComesOverTheTopAndDownInFrontInsideTheStrikeWindow()
    {
        var curve = SwingCurve.Blade(SwingClip);
        float a = SwingClip.StrikeStart!.Value, b = SwingClip.StrikeEnd!.Value;
        Assert.Equal(SwingCurve.WindupDeg, curve.Evaluate(a), 3);
        Assert.Equal(SwingCurve.StruckDeg, curve.Evaluate(b), 3);
        Assert.True(curve.Evaluate(0f) > 90f, "the swing starts with the blade behind the head");
        // Straight ahead (0°) is passed inside the strike window, and the strike only ever comes down.
        float forward = -1f;
        for (int i = 0; i <= 200; i++)
        {
            float t = a + (b - a) * i / 200f, next = a + (b - a) * (i + 1) / 200f;
            if (forward < 0 && curve.Evaluate(t) <= 0f) forward = t;
            if (i < 200) Assert.True(curve.Evaluate(next) <= curve.Evaluate(t) + 1e-4f, $"the strike rose at t {t:0.000}");
        }
        Assert.InRange(forward, a, b);
        // Continuous: no jumps anywhere over the use.
        for (int i = 0; i < 1000; i++)
            Assert.True(MathF.Abs(curve.Evaluate((i + 1) / 1000f) - curve.Evaluate(i / 1000f)) < 2f, $"jump at t {i / 1000f:0.000}");
    }

    [Fact]
    public void TheHitboxSweepsTheArcInTimeOrder()
    {
        var curve = SwingCurve.Blade(SwingClip);
        var plane = new SwingPlane(Vector3.Zero, 0f, SwingPlane.SwingReach(3f, 1f));
        // Small boxes on the arc, 1.4 m from the pivot: overhead, ahead and up, ahead, low ahead.
        float[] angles = { 110f, 50f, 0f, -40f };
        var first = new int[angles.Length];
        const int duration = 25;
        for (int k = 0; k < angles.Length; k++)
        {
            var c = plane.Point(angles[k], 1.4f);
            var half = new Vector3(0.05f);
            first[k] = -1;
            float prev = curve.Evaluate(SwingClip.StrikeStart!.Value);
            for (int tick = 1; tick <= duration && first[k] < 0; tick++)
            {
                float t = MathF.Max(tick / (float)duration, SwingClip.StrikeStart!.Value);
                float now = curve.Evaluate(t);
                if (plane.Sweeps(prev, now, c - half, c + half, out _)) first[k] = tick;
                prev = now;
            }
            Assert.True(first[k] > 0, $"the blade never passed {angles[k]}°");
        }
        for (int k = 1; k < angles.Length; k++) Assert.True(first[k] >= first[k - 1], $"{angles[k]}° was hit before {angles[k - 1]}°");
        Assert.True(first[^1] > first[0]);
    }

    [Fact]
    public void AThrustReachesFullExtensionAtTheStrikeEndAndHoldsIt()
    {
        var curve = SwingCurve.Extension(ThrustClip);
        float a = ThrustClip.StrikeStart!.Value, b = ThrustClip.StrikeEnd!.Value;
        Assert.Equal(0f, curve.Evaluate(a * 0.5f));
        Assert.Equal(1f, curve.Evaluate(b), 4);
        Assert.Equal(1f, curve.Evaluate(b + 0.05f), 4);
        Assert.True(curve.Evaluate(b - 0.05f) < 1f);
        Assert.Equal(0f, curve.Evaluate(1f), 4);
        float live = curve.FirstReach(MeleeSystem.ThrustLive);
        Assert.InRange(live, a, b);
    }

    [Fact]
    public void ASizePrefixExtendsTheReach()
    {
        var curve = SwingCurve.Blade(SwingClip);
        float normal = SwingPlane.SwingReach(3f, 1f), massive = SwingPlane.SwingReach(3f, 1.18f);
        Assert.True(massive > normal);
        var small = new SwingPlane(Vector3.Zero, 0f, normal);
        var large = new SwingPlane(Vector3.Zero, 0f, massive);
        // A box just past the normal blade's reach straight ahead.
        var c = small.Point(0f, normal + SwingPlane.BladeHalfWidth + SwingPlane.LateralTolerance + 0.2f);
        var half = new Vector3(0.05f);
        Assert.False(small.Sweeps(90f, -45f, c - half, c + half, out _));
        Assert.True(large.Sweeps(90f, -45f, c - half, c + half, out float deg));
        Assert.InRange(deg, -15f, 15f);
        Assert.True(curve.Evaluate(0.3f) is > -45f and < 165f);
    }

    [Fact]
    public void TheSwingIsForgivingOffItsPlaneButNotFarToTheSide()
    {
        Assert.True(HitsAt(0.4f, 1.0f), "a slime 0.4 m to the side was missed");
        Assert.True(HitsAt(-0.4f, 1.0f), "a slime 0.4 m to the other side was missed");
        Assert.False(HitsAt(1.5f, 1.0f), "a slime 1.5 m to the side was hit");
        Assert.False(HitsAt(-1.5f, 1.0f), "a slime 1.5 m to the other side was hit");
    }

    [Theory]
    [InlineData("wooden_sword", "zombie", 1.2f)]
    [InlineData("breaker_blade", "green_slime", 1.5f)]
    [InlineData("breaker_blade", "green_slime", 2.0f)]
    [InlineData("breaker_blade", "green_slime", 2.5f)]
    public void TheBladeRaisedBehindTheHeadNeverHitsCreaturesBehind(string item, string enemy, float behind)
    {
        using var g = Arena();
        var npc = g.World.Npcs.Spawn(g.Content.Enemies.Get(enemy), g.Player.Position + new Vector3(0, 0.05f, -behind))!;
        npc.Life = npc.MaxLife = 5000;
        npc.Timer = -100_000; // sit still
        g.Hold(item);
        g.Player.Aim = Vector3.UnitZ;
        Assert.Equal(UseOutcome.Swung, g.World.ItemUse.Use(g.Player, null, true));
        g.Ticks(g.Player.UseAnimationMax + 1);
        Assert.Equal(5000, npc.Life);
    }

    [Fact]
    public void ASpearReachesFurtherThanAShortswordOnlyOnceExtended()
    {
        Assert.Equal(-1, ThrustHitTick("copper_shortsword", 2.3f, out _));
        int tick = ThrustHitTick("spear", 2.3f, out int duration);
        Assert.True(tick > 0, "the spear missed a slime at 2.3 m");
        float ext = SwingCurve.Extension(ThrustClip).Evaluate(tick / (float)duration);
        Assert.True(ext >= MeleeSystem.ThrustLive, $"hit at extension {ext:0.00}");
    }

    [Fact]
    public void ASpeedPrefixHitsAtTheSamePointOfTheArc()
    {
        var (normalTick, normalDuration, normalDeg) = SwingHit(null);
        var (quickTick, quickDuration, quickDeg) = SwingHit("quick");
        Assert.True(quickDuration < normalDuration);
        Assert.True(quickTick <= normalTick);
        Assert.InRange(quickTick / (float)quickDuration - normalTick / (float)normalDuration, -0.12f, 0.12f);
        Assert.InRange(quickDeg - normalDeg, -40f, 40f);
    }

    [Fact]
    public void SwingsAreDeterministic()
    {
        var a = SwingHit("legendary");
        var b = SwingHit("legendary");
        Assert.Equal(a, b);
        Assert.Equal(SwingCurve.Blade(SwingClip).Evaluate(0.37f), SwingCurve.Blade(SwingClip).Evaluate(0.37f));
    }

    private static TestGame Arena()
    {
        var g = TestGame.Create();
        g.World.Spawner.Enabled = false;
        return g;
    }

    private static Npc Dummy(TestGame g, float side, float ahead)
    {
        var npc = g.World.Npcs.Spawn(g.Content.Enemies.Get("green_slime"), g.Player.Position + new Vector3(side, 0.05f, ahead))!;
        npc.Life = npc.MaxLife = 5000;
        npc.Timer = -100_000; // sit still
        return npc;
    }

    private static bool HitsAt(float side, float ahead)
    {
        using var g = Arena();
        var gel = Dummy(g, side, ahead);
        g.Hold("wooden_sword");
        g.Player.Aim = Vector3.UnitZ;
        Assert.Equal(UseOutcome.Swung, g.World.ItemUse.Use(g.Player, null, true));
        g.Ticks(g.Player.UseAnimationMax + 1);
        return gel.Life < 5000;
    }

    private static int ThrustHitTick(string item, float ahead, out int duration)
    {
        using var g = Arena();
        var gel = Dummy(g, 0f, ahead);
        g.Hold(item);
        g.Player.Aim = Vector3.UnitZ;
        Assert.Equal(UseOutcome.Swung, g.World.ItemUse.Use(g.Player, null, true));
        duration = g.Player.UseAnimationMax;
        for (int tick = 1; tick <= duration; tick++)
        {
            g.World.Tick();
            if (gel.Life < 5000) return tick;
        }
        return -1;
    }

    private static (int Tick, int Duration, float Deg) SwingHit(string? prefix)
    {
        using var g = Arena();
        var gel = Dummy(g, 0f, 1.5f);
        var sword = g.Content.Items.Get("wooden_sword");
        g.Player.State.Inventory.Backpack[0] = new ItemStack(sword, 1, prefix == null ? null : g.Content.Prefixes.Get(prefix));
        g.Player.State.Inventory.Select(0);
        g.Player.Aim = Vector3.UnitZ;
        Assert.Equal(UseOutcome.Swung, g.World.ItemUse.Use(g.Player, null, true));
        int duration = g.Player.UseAnimationMax;
        for (int tick = 1; tick <= duration; tick++)
        {
            g.World.Tick();
            if (gel.Life < 5000) return (tick, duration, g.World.Melee.LastHit!.Value.BladeDeg);
        }
        return (-1, duration, float.NaN);
    }
}
