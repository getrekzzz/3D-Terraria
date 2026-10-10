using Godot;
using Strata.Core.Gameplay.Player;
using Strata.Game.Audio;

namespace Strata.Game.Player;

/// <summary>The body's own sounds: footsteps on whatever is underfoot, jumps and landings, extra jumps, dashes, wing beats
/// or rocket bursts while flying, and the grappling hook's chain.</summary>
public partial class PlayerController
{
    private bool _soundsHooked;
    private bool _wasOnFloor = true;
    private float _airVelocityY;
    private float _stepTimer;
    private float _flightTimer;
    private HookState _lastHook;

    private void UpdateSounds(float dt)
    {
        if (!_soundsHooked)
        {
            _soundsHooked = true;
            AirJumped += () => Sfx.Play("double_jump");
            Dashed += () => Sfx.Play("dash");
            Jumped += () => Sfx.Play("jump"); // only a real jump: knockback and launches no longer sound like one
            // Fall damage goes through Core (PlayerController.Fall.cs), so SoundEvents plays its hurt sound like any other.
        }
        bool onFloor = IsOnFloor();
        var v = Velocity;
        if (!_wasOnFloor && onFloor)
        {
            if (_airVelocityY < -6f) Sfx.Play("land", Mathf.Clamp((-_airVelocityY - 6f) * 0.8f - 10f, -12f, 0f));
            if (_airVelocityY < -14f) CameraShake.Punch(0.15f);
            Landed?.Invoke(-_airVelocityY);
        }
        _airVelocityY = onFloor ? 0f : v.Y;

        float speed = new Vector2(v.X, v.Z).Length();
        if (onFloor && speed > 1.2f && !_swimming)
        {
            _stepTimer -= dt * speed;
            if (_stepTimer <= 0f)
            {
                _stepTimer = 1.6f; // metres between steps
                Sfx.Play(StepSound(), -4f, 0.1f);
                Footstep?.Invoke(speed);
            }
        }
        else _stepTimer = Mathf.Min(_stepTimer, 0.4f);

        if (Flying)
        {
            _flightTimer -= dt;
            if (_flightTimer <= 0f)
            {
                _flightTimer = RocketBurning ? 0.22f : 0.32f;
                Sfx.Play(RocketBurning ? "rocket" : "wings", -6f);
            }
        }
        else _flightTimer = 0f;

        if (Hook == HookState.Flying && _lastHook != HookState.Flying) Sfx.Play("grapple");
        _lastHook = Hook;
        _wasOnFloor = onFloor;
    }

    /// <summary>The block under the feet, or null over air (or unloaded cells).</summary>
    public Strata.Core.Content.Defs.BlockDef? GroundBlock()
    {
        var session = _runtime.Session;
        var feet = GlobalPosition + Vector3.Down * 0.1f;
        var cell = new Strata.Core.Common.Int3(Mathf.FloorToInt(feet.X / 0.5f), Mathf.FloorToInt(feet.Y / 0.5f), Mathf.FloorToInt(feet.Z / 0.5f));
        ushort id = session.World.GetBlock(cell);
        return id == 0 || id >= session.Content.Blocks.Count ? null : session.Content.Blocks[id];
    }

    /// <summary>Footstep sound for the block underfoot (grass, sand, snow, wood; stone for the rest).</summary>
    private string StepSound()
    {
        if (GroundBlock() is not { } ground) return "step_stone";
        return ground.Sound switch
        {
            "grass" or "dirt" or "cloth" => "step_grass",
            "sand" => "step_sand",
            "snow" or "ice" => "step_snow",
            "wood" => "step_wood",
            _ => "step_stone",
        };
    }
}
