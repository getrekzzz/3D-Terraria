using Godot;

namespace Strata.Game.Player;

public sealed partial class PlayerController
{
    private void TrackFall()
    {
        float y = GlobalPosition.Y;
        if (!IsOnFloor() && !_swimming && !_climbing)
        {
            if (!_airborne)
            {
                _airborne = true;
                _peakY = y;
            }
            _peakY = Mathf.Max(_peakY, y);
            return;
        }
        if (_airborne && !_swimming)
        {
            int damage = _config.FallDamage(_peakY - y, Entity.State.Effects.NoFallDamage);
            // Through Core, so the fall shows a number and sounds like a hurt.
            if (damage > 0) _runtime.Game.HurtPlayerDirect(Entity, damage);
        }
        _airborne = false;
        _peakY = y;
    }
}
