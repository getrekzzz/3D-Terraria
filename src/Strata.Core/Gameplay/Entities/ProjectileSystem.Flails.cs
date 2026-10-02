using System.Numerics;
using Strata.Core.Common;
using Strata.Core.Physics;
using Strata.Core.Session;

namespace Strata.Core.Gameplay.Entities;

/// <summary>
/// Flails (P3-13): held, the ball whirls around the hand on its chain (1.1 m out, 12 rad/s, in a plane tipped toward
/// the aim), striking what it sweeps every 12 ticks; let go, it flies out along the aim at the tether's speed, drops
/// after 0.3 s and drags along the ground, and is reeled back after 1.5 s, at the end of its chain, or when the button
/// is pressed again.
/// </summary>
public sealed partial class ProjectileSystem
{
    private const float FlailOrbit = 1.1f, FlailTurn = 12f, FlailGravity = 14f, FlailDrag = 6f;
    private const int FlailRehit = 12, FlailFloat = 18, FlailOut = 90;

    private bool TickFlail(Projectile p, PlayerEntity owner, float dt)
    {
        var tether = p.Def.Tether!;
        bool holding = owner.State.Inventory.Held.Item == p.Source;
        if (!holding) return ReelIn(p);
        if (!p.Launched)
        {
            if (!owner.UseHeld)
            {
                p.Launched = true;
                p.LaunchedAge = 0;
                p.Position = FlailHand(owner) + owner.Aim * 0.4f;
                p.Velocity = owner.Aim * tether.Speed;
                p.HitNpcs.Clear();
                return true;
            }
            // Whirl in a plane tipped 30° from level toward the aim, so the ball swings past the face and the feet.
            var right = Vector3.Cross(owner.Aim, Vector3.UnitY);
            right = right.LengthSquared() > 1e-4f ? Vector3.Normalize(right) : Vector3.UnitX;
            var axis = Vector3.Normalize(Vector3.UnitY * 0.87f + Vector3.Cross(right, Vector3.UnitY) * -0.5f);
            var u = Vector3.Normalize(Vector3.Cross(axis, right));
            float a = p.Age * FlailTurn / Units.TicksPerSecond;
            var at = FlailHand(owner) + (right * MathF.Cos(a) + u * MathF.Sin(a)) * FlailOrbit;
            p.Velocity = (at - p.Position) / dt;
            p.Position = at;
        }
        else
        {
            p.LaunchedAge++;
            float range = tether.Range * Units.CellSize;
            bool pressedAgain = owner.UseHeld && p.LaunchedAge > 6;
            if (p.LaunchedAge > FlailOut || Vector3.Distance(p.Position, owner.Eye) > range || pressedAgain) return ReelIn(p);
            if (p.LaunchedAge > FlailFloat) p.Velocity.Y -= FlailGravity * dt;
            float r = MathF.Min(p.Def.Radius * Units.CellSize, 0.2f);
            float fallBefore = p.Velocity.Y;
            VoxelMotion.Move(_world.Session.World, ref p.Position, ref p.Velocity, new Vector3(r), dt);
            if (fallBefore < 0 && p.Velocity.Y == 0)
            {
                // On the ground: it drags, slowing.
                float k = MathF.Max(0f, 1f - FlailDrag * dt);
                p.Velocity.X *= k;
                p.Velocity.Z *= k;
            }
        }
        if (p.Def.Contact)
        {
            if (p.Age % FlailRehit == 0) p.HitNpcs.Clear();
            HitCreatures(p);
        }
        return true;
    }

    /// <summary>Where the flail's chain is held: the right hand, a little ahead of the chest.</summary>
    private static Vector3 FlailHand(PlayerEntity owner)
    {
        var flat = new Vector3(owner.Aim.X, 0, owner.Aim.Z);
        flat = flat.LengthSquared() > 1e-4f ? Vector3.Normalize(flat) : Vector3.UnitZ;
        return owner.Eye - Vector3.UnitY * 0.35f + flat * 0.25f;
    }

    /// <summary>Sends a tethered projectile back to the hand (the returning path, with its own clock).</summary>
    private static bool ReelIn(Projectile p)
    {
        p.Returning = true;
        p.Age = 0;
        return true;
    }
}
