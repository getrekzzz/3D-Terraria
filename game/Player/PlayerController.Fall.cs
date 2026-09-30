using Godot;

namespace Strata.Game.Player;

public sealed partial class PlayerController
{
    /// <summary>Raised with fall damage taken on landing.</summary>
    public event System.Action<int>? FallDamaged;

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
            if (damage > 0)
            {
                Entity.State.Vitals.DamageDirect(damage);
                FallDamaged?.Invoke(damage);
            }
        }
        _airborne = false;
        _peakY = y;
    }
}
