using Godot;
using Strata.Core.Session;

namespace Strata.Game.Player;

/// <summary>
/// The player's body crumples into a ragdoll when they die (X-16), thrown along the last motion (the killing blow's
/// knockback), and lies there until the respawn hands the bones back to animation.
/// </summary>
public sealed partial class PlayerAvatar
{
    private Vector3 _lastMotion;
    private Rendering.Ragdoll? _lying;

    /// <summary>The body lies as a ragdoll (probe <c>avatar.ragdoll</c>).</summary>
    public bool Ragdolled => _lying != null;

    private void UpdateRagdoll(PlayerEntity entity)
    {
        if (entity.Alive)
        {
            _lastMotion = _controller.Velocity;
            if (_lying == null) return;
            _lying.Stop();
            _lying = null;
            if (_visual?.Humanoid != null) _visual.Humanoid.Animating = true;
            return;
        }
        if (_lying != null || _visual?.Humanoid is not { } h || _visual.Ragdoll is not { } ragdoll) return;
        h.Animating = false;
        var push = new Vector3(_lastMotion.X, Mathf.Max(_lastMotion.Y, 0f), _lastMotion.Z);
        if (push.LengthSquared() > 64f) push = push.Normalized() * 8f;
        ragdoll.Start(push, Basis.Z * 1.5f); // a still body folds backwards
        _lying = ragdoll;
    }
}
