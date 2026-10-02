using System;
using System.Collections.Generic;
using System.Numerics;
using Strata.Core.Gameplay.Entities;
using Strata.Core.Gameplay.Interaction;
using Strata.Core.Tests.Support;

namespace Strata.Core.Tests.Gameplay;

public class AimAssistTests
{
    private static readonly AimAssistConfig Config = new();

    private static Npc Make(string id, Vector3 feet) => new(1, TestPaths.Content.Enemies.Get(id), feet);

    /// <summary>A zombie standing 10 m ahead (along −Z) of an eye at the origin, its centre at eye height.</summary>
    private static Npc ZombieAhead(float sideways = 0f)
    {
        var z = Make("zombie", new Vector3(sideways, 0, -10));
        z.Position = new Vector3(sideways, -z.HalfExtents.Y, -10);
        return z;
    }

    [Fact]
    public void FrictionIsStrongestOnTheTargetAndFadesOut()
    {
        float radius = 0.05f;
        Assert.Equal(Config.Friction, AimAssist.Friction(0f, radius, Config), 4);
        Assert.Equal(Config.Friction, AimAssist.Friction(radius, radius, Config), 4);
        Assert.Equal(1f, AimAssist.Friction(radius * Config.FrictionReach + 0.01f, radius, Config), 4);
        float previous = 0f;
        for (float a = 0; a < radius * 3f; a += radius / 10f)
        {
            float f = AimAssist.Friction(a, radius, Config);
            Assert.True(f >= previous - 1e-5f, $"friction must ease out monotonically (at {a})");
            Assert.InRange(f, Config.Friction, 1f);
            previous = f;
        }
    }

    [Fact]
    public void PicksTheCreatureNearestTheCrosshairAndIgnoresTheRest()
    {
        var onAxis = ZombieAhead(0.6f);
        var wide = ZombieAhead(6f); // ~31° off: outside the cone
        var bunny = Make("bunny", new Vector3(0, -0.2f, -8)); // a critter, dead centre
        var far = Make("zombie", new Vector3(0, -1, -80)); // out of range
        var dead = ZombieAhead(0.1f);
        dead.Life = 0;
        var list = new List<Npc> { wide, bunny, far, dead, onAxis };
        var pick = AimAssist.Pick(list, Vector3.Zero, new Vector3(0, 0, -1), Config);
        Assert.NotNull(pick);
        Assert.Same(onAxis, pick!.Value.Npc);
        Assert.True(pick.Value.AngularRadius > 0);
        Assert.Null(AimAssist.Pick(new List<Npc> { wide, bunny, far, dead }, Vector3.Zero, new Vector3(0, 0, -1), Config));
    }

    [Fact]
    public void MagnetismTurnsTowardTheTargetOnlyWhileAiming()
    {
        // The target is ahead and to the right (−X is left at yaw 0), so the yaw must decrease.
        var target = new Vector3(1.5f, 0.3f, -10f);
        var (yaw, pitch) = AimAssist.Pull(0f, 0f, Vector3.Zero, target, 0.02f, 1f, 1f / 60f, Config);
        Assert.True(yaw < 0, $"yaw {yaw}");
        Assert.True(pitch > 0, $"pitch {pitch}");
        // Capped at maxPullDegrees per second at full input.
        float step = MathF.Sqrt(yaw * yaw + pitch * pitch);
        Assert.True(step <= Config.MaxPullDegrees * MathF.PI / 180f / 60f + 1e-5f);
        // Resting sticks: nothing; already on the target: nothing.
        Assert.Equal((0f, 0f), AimAssist.Pull(0f, 0f, Vector3.Zero, target, 0.02f, 0f, 1f / 60f, Config));
        Assert.Equal((0f, 0f), AimAssist.Pull(0f, 0f, Vector3.Zero, new Vector3(0, 0, -10), 0.05f, 1f, 1f / 60f, Config));
    }

    [Fact]
    public void RepeatedPullsConvergeWithoutOvershoot()
    {
        var target = new Vector3(2f, 1f, -10f);
        float yaw = 0, pitch = 0;
        for (int frame = 0; frame < 600; frame++)
        {
            var (dy, dp) = AimAssist.Pull(yaw, pitch, Vector3.Zero, target, 0.02f, 1f, 1f / 60f, Config);
            yaw += dy;
            pitch += dp;
        }
        var dir = AimAssist.Forward(yaw, pitch);
        float off = MathF.Acos(Math.Clamp(Vector3.Dot(dir, Vector3.Normalize(target)), -1f, 1f));
        Assert.True(off <= 0.02f / 3f + 1e-3f, $"still {off} rad off");
        Assert.True(yaw > AimAssist.YawTo(target) - 1e-3f, "never swings past the target");
    }

    [Fact]
    public void ForwardAndAnglesAgree()
    {
        var d = Vector3.Normalize(new Vector3(-3, 2, -5));
        var f = AimAssist.Forward(AimAssist.YawTo(d), AimAssist.PitchTo(d));
        Assert.True(Vector3.Distance(d, f) < 1e-4f);
        Assert.True(Vector3.Distance(new Vector3(0, 0, -1), AimAssist.Forward(0, 0)) < 1e-6f);
    }
}
