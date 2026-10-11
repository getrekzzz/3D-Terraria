using System.Numerics;

namespace Strata.Core.Gameplay.Entities;

public sealed partial class ProjectileSystem
{
    /// <summary>How far ahead along the aim ray (seconds of flight) a guided shot aims, so it runs out along the ray once on it.</summary>
    private const float GuideLead = 0.3f;

    /// <summary>
    /// Magic Missile: while the owner holds the item that fired it, turns toward the point on the owner's aim ray at the
    /// projectile's own distance from their eye (plus a short lead), keeping its speed; let go and it flies straight.
    /// </summary>
    private void Guide(Projectile p, float dt)
    {
        var owner = p.Owner;
        if (owner is not { Alive: true } || p.Source == null || owner.State.Inventory.Held.Item != p.Source) return;
        float speed = p.Velocity.Length();
        if (speed < 1e-3f) return;
        var eye = owner.Eye;
        var goal = eye + owner.Aim * (Vector3.Distance(p.Position, eye) + speed * GuideLead);
        var to = goal - p.Position;
        if (to.LengthSquared() < 1e-6f) return;
        var dir = Vector3.Lerp(p.Velocity / speed, Vector3.Normalize(to), MathF.Min(1f, p.Def.Guided * dt));
        if (dir.LengthSquared() < 1e-6f) return;
        p.Velocity = Vector3.Normalize(dir) * speed;
    }
}
