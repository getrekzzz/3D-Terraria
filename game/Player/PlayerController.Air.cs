using Godot;
using Strata.Core.Common;
using Strata.Core.Gameplay.Stats;

namespace Strata.Game.Player;

/// <summary>
/// Movement accessories: extra jumps in the air (Cloud in a Bottle and friends, one each per flight), wings and rocket
/// boots (rise while jump is held; wings then glide), double-tap dashes, the speed-boot run-up and the jump-apex
/// probe. Everything refills on landing.
/// </summary>
public sealed partial class PlayerController
{
    private const float DoubleTapWindow = 0.28f;
    private int _airJumpsUsed;
    private float _wingTime;
    private float _bootTime;
    private float _dashCooldown;
    private float _dashTime;
    private readonly float[] _lastTap = { -1f, -1f, -1f, -1f };
    private float _clock;
    private float _runUp;
    private float _jumpStartY;
    private bool _jumpTracking;

    /// <summary>Height (cells) the last ground jump has reached above its takeoff (smoke probe).</summary>
    public float JumpApexCells { get; private set; }

    /// <summary>Seconds of wing and boot flight left (HUD).</summary>
    public float FlightLeft => _wingTime + _bootTime;

    /// <summary>Flying with wings or boots this tick (sprite trails, horizontal speed).</summary>
    public bool Flying { get; private set; }

    /// <summary>Gliding on spent wings (slow fall).</summary>
    public bool Gliding { get; private set; }

    /// <summary>Rocket boots are burning this tick (flames at the feet).</summary>
    public bool RocketBurning { get; private set; }

    private void RefillAirMovement()
    {
        var fx = Entity.State.Effects;
        _airJumpsUsed = 0;
        _wingTime = (fx.Wings?.Time ?? 0) / (float)Units.TicksPerSecond;
        _bootTime = (fx.RocketBoots?.Time ?? 0) / (float)Units.TicksPerSecond;
    }

    /// <summary>A jump pressed in the air: spends the next extra jump. Returns false when none is left.</summary>
    private bool TryAirJump(ref Vector3 v)
    {
        var jumps = Entity.State.Effects.ExtraJumps;
        if (_airJumpsUsed >= jumps.Count) return false;
        float cells = jumps[_airJumpsUsed++];
        v.Y = Mathf.Sqrt(2f * _config.Gravity * cells * Units.CellSize);
        _peakY = GlobalPosition.Y;
        AirJumped?.Invoke();
        return true;
    }

    /// <summary>Top running speed: speed boots ease in over the run-up, and Sprint only boosts their bonus.</summary>
    private float RunSpeed(StatSheet stats)
    {
        bool sprint = !UI.InputGate.GameplayBlocked && Input.IsActionPressed("sprint");
        return _config.TopSpeed(stats.Bonus(StatIds.MoveSpeed), stats.Bonus(StatIds.RunSpeed), _runUp, sprint);
    }

    private void TrackRunUp(float dt, bool onFloor, Vector3 wish, Vector3 horizontal, StatSheet stats)
    {
        float walk = _config.WalkSpeed * (1f + stats.Bonus(StatIds.MoveSpeed)) * stats.Multiplier(StatIds.MoveSpeed);
        _runUp = _config.AdvanceRunUp(_runUp, dt, onFloor, wish.LengthSquared() > 0.01f, horizontal.Length(), walk);
    }

    private void StartJumpApex()
    {
        _jumpStartY = GlobalPosition.Y;
        _jumpTracking = true;
        JumpApexCells = 0;
    }

    /// <summary>Follows the rising ground jump until it lands (called from the fall tracker).</summary>
    private void TrackJumpApex(float y, bool airborne)
    {
        if (!_jumpTracking) return;
        JumpApexCells = Mathf.Max(JumpApexCells, (y - _jumpStartY) / Units.CellSize);
        if (!airborne) _jumpTracking = false;
    }

    /// <summary>Raised on an extra jump (puff of cloud).</summary>
    public event System.Action? AirJumped;

    /// <summary>
    /// Wings, then rocket boots, lift the player while jump is held once the ground jump's own lift is over; spent
    /// wings glide. Runs after gravity.
    /// </summary>
    private void UpdateFlight(float dt, bool held, ref Vector3 v)
    {
        var fx = Entity.State.Effects;
        Flying = false;
        Gliding = false;
        RocketBurning = false;
        if (!held || _jumpHold > 0) return;
        var source = _wingTime > 0 ? fx.Wings : _bootTime > 0 ? fx.RocketBoots : null;
        if (source != null)
        {
            if (_wingTime > 0) _wingTime -= dt;
            else
            {
                _bootTime -= dt;
                RocketBurning = true;
            }
            v.Y = Mathf.MoveToward(v.Y, source.MaxRise, (source.Ascent + _config.Gravity) * dt);
            Flying = true;
            _peakY = GlobalPosition.Y; // no fall damage from a flight
            return;
        }
        if (fx.Wings is { Glide: > 0 } wings && v.Y < -wings.Glide)
        {
            v.Y = -wings.Glide;
            Gliding = true;
            _peakY = GlobalPosition.Y;
        }
    }

    /// <summary>Horizontal speed multiplier while flying or gliding.</summary>
    private float AirSpeedFactor()
    {
        var fx = Entity.State.Effects;
        if (Flying || Gliding) return Mathf.Max(fx.Wings?.AirSpeed ?? 1f, fx.RocketBoots?.AirSpeed ?? 1f);
        return 1f;
    }

    /// <summary>Double-tapping a direction dashes that way (with a dash accessory). Returns true while dashing.</summary>
    private bool UpdateDash(float dt, ref Vector3 horizontal)
    {
        _clock += dt;
        _dashCooldown -= dt;
        if (_dashTime > 0)
        {
            _dashTime -= dt;
            return true;
        }
        var dash = Entity.State.Effects.Dash;
        if (dash == null || UI.InputGate.GameplayBlocked) return false;
        string[] actions = { "move_forward", "move_back", "move_left", "move_right" };
        Vector3[] local = { Vector3.Forward, Vector3.Back, Vector3.Left, Vector3.Right };
        for (int i = 0; i < actions.Length; i++)
        {
            if (!Input.IsActionJustPressed(actions[i])) continue;
            bool doubleTap = _clock - _lastTap[i] < DoubleTapWindow;
            _lastTap[i] = _clock;
            if (!doubleTap || _dashCooldown > 0) continue;
            horizontal = (Rig.YawBasis * local[i]).Normalized() * dash.Speed;
            _dashCooldown = dash.Cooldown / (float)Units.TicksPerSecond;
            _dashTime = 0.22f;
            _lastTap[i] = -1f;
            Dashed?.Invoke();
            return true;
        }
        return false;
    }

    /// <summary>Raised when a dash starts.</summary>
    public event System.Action? Dashed;
}
