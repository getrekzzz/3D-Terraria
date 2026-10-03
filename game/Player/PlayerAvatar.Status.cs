using Godot;
using Strata.Game.Rendering;

namespace Strata.Game.Player;

/// <summary>
/// Buff and debuff looks on the player's body (StatusVisuals): the visual statuses' tint multiplied into the model and
/// their looping particles following the body (hidden with it in first person).
/// </summary>
public sealed partial class PlayerAvatar
{
    /// <summary>Carrier key of the local player's statuses (creature ids are positive).</summary>
    private const long StatusCarrier = -1;
    private static readonly Vector3 StatusHalfExtents = new(0.3f, 0.85f, 0.3f);

    private void UpdateStatus()
    {
        var entity = _controller.Entity;
        var buffs = entity.State.Buffs;
        _visual!.SetTint(StatusVisuals.Tint(buffs, Colors.White, 0.37f));
        if (entity.Alive && buffs.Active.Count > 0)
            StatusVisuals.Instance?.Track(StatusCarrier, buffs, GlobalPosition + Vector3.Up * 0.9f, StatusHalfExtents, !_controller.Rig.FirstPerson);
    }
}
