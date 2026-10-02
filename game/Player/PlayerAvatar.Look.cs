using Strata.Core.Gameplay.Player;

namespace Strata.Game.Player;

/// <summary>
/// The look changing in play: a haircut or a hair dye re-applies the appearance (the hair model swaps in place on the
/// same body), and a dynamic dye (Life, Mana, Depth, Money, Time, Speed, Rainbow) is re-tinted twice a second.
/// </summary>
public sealed partial class PlayerAvatar
{
    private const double DyeInterval = 0.5;
    private double _dyeTimer;
    private bool _dynamicDye;
    private Appearance? _tinted;

    /// <summary>The body is a generated player model that takes the look's colours (not the fallback mesh).</summary>
    private bool _lookable;

    private void InitializeLook(bool lookable)
    {
        _lookable = lookable;
        var state = _controller.Entity.State;
        state.LookChanged += ApplyLook;
        TreeExiting += () => state.LookChanged -= ApplyLook;
        ApplyLook();
    }

    /// <summary>Applies the current look with the dye's colour (if any) as the hair colour.</summary>
    private void ApplyLook()
    {
        if (_visual == null || !_lookable) return;
        var entity = _controller.Entity;
        var dye = HairDyes.Worn(entity, _game);
        _dynamicDye = dye != null && HairDyes.IsDynamic(dye);
        _dyeTimer = DyeInterval;
        if (dye == null)
        {
            _visual.SetAppearance(entity.State.Look);
            return;
        }
        _tinted = entity.State.Look.Clone();
        _tinted.HairColor = HairDyes.ColorFor(entity, _game).Hex;
        _visual.SetAppearance(_tinted);
    }

    private void UpdateLook(double delta)
    {
        if (!_dynamicDye || _visual == null || _tinted == null) return;
        _dyeTimer -= delta;
        if (_dyeTimer > 0) return;
        _dyeTimer = DyeInterval;
        string hex = HairDyes.ColorFor(_controller.Entity, _game).Hex;
        if (hex == _tinted.HairColor) return;
        _tinted.HairColor = hex;
        _visual.SetAppearance(_tinted);
    }
}
