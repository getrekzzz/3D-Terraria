using System.Numerics;
using Strata.Core.Gameplay.Entities;
using Strata.Core.Session;

namespace Strata.Core.Gameplay.Mounts;

/// <summary>What the rider asks of the mount this step.</summary>
/// <param name="Wish">Horizontal direction wanted (length 0–1, world x/z).</param>
public readonly record struct MountInput(Vector3 Wish, bool JumpPressed, bool JumpHeld, bool Descend);

/// <summary>
/// Mounted movement, engine independent: the engine passes the body's velocity and floor contact each physics step,
/// gets the new velocity back and moves the body with it. Mounts run at their own speed, jump their own height (a held
/// jump keeps gravity light for a moment), fly while jump is held for their flight time (refilled on landing), or fly
/// freely and hover (the UFO). Mounted riders take no fall damage.
/// </summary>
public static class MountMotion
{
    /// <summary>Gravity while the jump is held early in a jump (fraction of full).</summary>
    public const float HoldGravity = 0.45f;

    public static Vector3 Step(MountRide ride, Vector3 velocity, bool onFloor, MountInput input, float gravity, float dt)
    {
        var c = ride.Config;
        var horizontal = new Vector3(velocity.X, 0, velocity.Z);
        var wish = input.Wish;
        if (wish.LengthSquared() > 1f) wish = Vector3.Normalize(wish);
        float accel = onFloor ? c.Acceleration : c.AirAcceleration;
        horizontal = MoveToward(horizontal, wish * c.RunSpeed, accel * dt);
        float vy = velocity.Y;

        if (onFloor)
        {
            ride.FlightLeft = c.FlightTime;
            ride.Flying = false;
            if (input.JumpPressed)
            {
                vy = c.JumpSpeed;
                ride.HoldLeft = c.JumpHoldTime;
            }
        }
        if (!input.JumpHeld) ride.HoldLeft = 0f;

        bool canFly = !onFloor && c.FlightTime != 0f && (ride.FliesFreely || ride.FlightLeft > 0f);
        if (canFly && ride.FliesFreely)
        {
            ride.Flying = true;
            float target = input.JumpHeld ? c.FlySpeed : input.Descend ? -c.FlySpeed : c.Hover ? 0f : vy - gravity * c.GravityScale * dt;
            vy = Approach(vy, target, c.FlySpeed * 4f * dt);
        }
        else if (canFly && input.JumpHeld && ride.HoldLeft <= 0f)
        {
            ride.Flying = true;
            ride.FlightLeft = MathF.Max(0f, ride.FlightLeft - dt);
            vy = Approach(vy, c.FlySpeed, c.FlySpeed * 4f * dt);
        }
        else if (!onFloor)
        {
            ride.Flying = false;
            float g = gravity * c.GravityScale * (ride.HoldLeft > 0f ? HoldGravity : 1f);
            ride.HoldLeft = MathF.Max(0f, ride.HoldLeft - dt);
            vy = MathF.Max(vy - g * dt, -c.MaxFallSpeed);
        }
        return new Vector3(horizontal.X, vy, horizontal.Z);
    }

    /// <summary>
    /// A mount that stomps (the Slimy Saddle) landing on a hostile creature: hurts it and bounces back up. Returns the
    /// upward speed to take, or null when nothing was under the rider's feet.
    /// </summary>
    public static float? TryStomp(GameWorld world, PlayerEntity rider, MountRide ride, float fallSpeed)
    {
        var c = ride.Config;
        if (c.StompDamage <= 0 || fallSpeed > -2f) return null;
        var feet = rider.Position;
        foreach (var npc in world.Npcs.All)
        {
            if (!npc.Alive || npc.Def.HasTag("critter")) continue;
            var center = npc.Center;
            var half = npc.HalfExtents;
            float top = center.Y + half.Y;
            if (feet.Y < top - 0.45f || feet.Y > top + 0.25f) continue;
            if (MathF.Abs(feet.X - center.X) > half.X + 0.45f || MathF.Abs(feet.Z - center.Z) > half.Z + 0.45f) continue;
            world.Npcs.Hit(npc, rider, c.StompDamage, Content.Defs.DamageClass.Melee, 0, 3f, feet, melee: true);
            return c.StompBounce;
        }
        return null;
    }

    private static Vector3 MoveToward(Vector3 from, Vector3 to, float step)
    {
        var d = to - from;
        float len = d.Length();
        return len <= step || len < 1e-6f ? to : from + d / len * step;
    }

    private static float Approach(float from, float to, float step) => from < to ? MathF.Min(from + step, to) : MathF.Max(from - step, to);
}
