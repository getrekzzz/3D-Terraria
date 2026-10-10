using System.Numerics;

namespace Strata.Core.Presentation;

/// <summary>
/// Secondary motion for hair, robes and coat tails (X-17): a damped spring that lags behind its anchor (a character's
/// head or hips), so loose parts swing back when the body accelerates and settle when it stops. The renderer bends the
/// parts by <see cref="Offset"/> (metres, world space), weighted per vertex; wind flutter is added in the shader.
/// Deterministic and allocation-free; a jump of the anchor (teleport, respawn) resets it instead of whipping.
/// </summary>
public struct SpringSway
{
    /// <summary>Stiffness (1/s²), damping (1/s), how much of the anchor's acceleration drives the parts (s²·…), and the
    /// largest offset (metres).</summary>
    public const float Stiffness = 60f, Damping = 7f, Drive = 0.012f, MaxOffset = 0.18f, TeleportSpeed = 40f;

    private Vector3 _lastAnchor, _lastVelocity, _velocity;
    private bool _started;

    /// <summary>Where the loose parts are pushed relative to their rest shape (world space, metres).</summary>
    public Vector3 Offset { get; private set; }

    public void Reset(Vector3 anchor)
    {
        _lastAnchor = anchor;
        _lastVelocity = _velocity = Offset = Vector3.Zero;
        _started = true;
    }

    /// <summary>Advances by <paramref name="dt"/> seconds with the anchor now at <paramref name="anchor"/>.</summary>
    public void Step(Vector3 anchor, float dt)
    {
        if (dt <= 0f) return;
        if (!_started)
        {
            Reset(anchor);
            return;
        }
        dt = MathF.Min(dt, 0.05f); // a hitch must not explode the spring
        var anchorVelocity = (anchor - _lastAnchor) / dt;
        if (anchorVelocity.Length() > TeleportSpeed)
        {
            Reset(anchor);
            return;
        }
        var acceleration = (anchorVelocity - _lastVelocity) / dt;
        _lastAnchor = anchor;
        _lastVelocity = anchorVelocity;
        var force = -Stiffness * Offset - Damping * _velocity - acceleration * (Drive * Stiffness);
        _velocity += force * dt;
        var offset = Offset + _velocity * dt;
        float length = offset.Length();
        if (length > MaxOffset)
        {
            offset *= MaxOffset / length;
            _velocity *= 0.5f;
        }
        Offset = offset;
    }
}
