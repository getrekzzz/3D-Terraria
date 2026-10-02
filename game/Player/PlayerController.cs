using Godot;
using Strata.Core.Common;
using Strata.Core.Gameplay.Player;
using Strata.Core.Gameplay.Stats;
using Strata.Core.Session;
using Strata.Game.World;

namespace Strata.Game.Player;

/// <summary>
/// The local player's body: momentum-style walking with step-up onto one-cell ledges, variable-height jumps with
/// coyote time and buffering, swimming, one-way platforms, fall damage, and syncing into the Core
/// <see cref="PlayerEntity"/>. The origin is at the feet.
/// </summary>
public sealed partial class PlayerController : CharacterBody3D
{
    private WorldRuntime _runtime = null!;
    private MovementConfig _config = null!;
    private float _coyote;
    private float _jumpBuffer;
    private float _jumpHold;
    private float _peakY;
    private bool _airborne;
    private bool _swimming;
    private bool _settle;
    private Strata.Core.Content.Defs.LiquidDef? _liquid;

    public PlayerEntity Entity { get; private set; } = null!;

    public WorldRuntime Runtime => _runtime;
    public CameraRig Rig { get; private set; } = null!;

    /// <summary>Waiting for terrain colliders before physics starts.</summary>
    public bool Frozen { get; private set; } = true;

    /// <summary>In a liquid deep enough to swim (animation).</summary>
    public bool Swimming => _swimming;

    /// <summary>Holding on to a rope (animation).</summary>
    public bool Climbing => _climbing;

    /// <summary>Re-resolve a standable spawn position once terrain around it is loaded.</summary>
    private bool _resolveSpawn = true;

    public void Initialize(WorldRuntime runtime, PlayerEntity entity, MovementConfig config)
    {
        _runtime = runtime;
        Entity = entity;
        Entity.HasBody = true; // this body advances the hook and applies its pull (UpdateGrapple)
        _config = config;
        Name = "Player";
        CollisionLayer = 0;
        CollisionMask = ColliderManager.TerrainLayer | ColliderManager.ObjectLayer | ColliderManager.PlatformLayer;
        FloorMaxAngle = Mathf.DegToRad(52f);
        FloorSnapLength = 0.3f;
        FloorStopOnSlope = true;
        FloorBlockOnWall = true;
        SafeMargin = 0.02f;
        var half = PlayerEntity.HalfExtents;
        var shape = new CollisionShape3D
        {
            Shape = new CapsuleShape3D { Radius = half.X - 0.05f, Height = half.Y * 2f },
            Position = new Vector3(0, half.Y, 0),
        };
        AddChild(shape);
        Rig = new CameraRig { Name = "CameraRig" };
        AddChild(Rig);
        Rig.Initialize(this);
    }

    public override void _Ready() => GlobalPosition = ToGodot(Entity.Position);

    /// <summary>Moves the body (and entity) to a feet position, clearing motion (spawn, teleport, respawn).</summary>
    /// <summary>
    /// Moves the body and freezes it until the area is loaded. <paramref name="resolveSpawn"/>: fall back to the
    /// spawn point if the spot is blocked (respawning); <paramref name="settle"/>: land on the nearest standable cell
    /// above or below (surface teleports whose height is an estimate).
    /// </summary>
    public void Teleport(System.Numerics.Vector3 feet, bool resolveSpawn = false, bool settle = false)
    {
        _resolveSpawn = resolveSpawn;
        _settle = settle;
        GlobalPosition = ToGodot(feet);
        Velocity = Vector3.Zero;
        Entity.Position = feet;
        Entity.Hook.Reset(); // a latched hook would yank the body back across the world
        _airborne = false;
        Frozen = true;
        ResetPhysicsInterpolation();
    }

    public override void _PhysicsProcess(double delta)
    {
        using var profile = Strata.Core.Diagnostics.Metrics.Frame.Time("gd.player_physics");
        float dt = (float)delta;
        if (Frozen)
        {
            if (!_runtime.IsAreaReady(CellAt(GlobalPosition + Vector3.Up * 0.2f))) return;
            if (_settle)
            {
                _settle = false;
                var at = CellAt(GlobalPosition + Vector3.Up * 0.2f);
                var ground = _runtime.Session.FindStandableNear(at);
                GlobalPosition = new Vector3((ground.X + 0.5f) * Units.CellSize, ground.Y * Units.CellSize, (ground.Z + 0.5f) * Units.CellSize);
                ResetPhysicsInterpolation();
                return; // the settled spot may still be loading
            }
            if (_resolveSpawn)
            {
                _resolveSpawn = false;
                var cell = CellAt(GlobalPosition + Vector3.Up * 0.2f);
                if (!_runtime.Session.IsStandable(cell.X, cell.Y, cell.Z, 4))
                {
                    var spawn = _runtime.Game.SpawnPositionFor(Entity.State);
                    GlobalPosition = ToGodot(spawn);
                    ResetPhysicsInterpolation();
                    return; // the new area may still be loading
                }
            }
            Frozen = false;
            _peakY = GlobalPosition.Y;
        }
        if (!Entity.Alive)
        {
            Velocity = Vector3.Zero;
            Entity.Minecart = null;
            return;
        }
        if (Entity.ExternalVelocity != System.Numerics.Vector3.Zero) Entity.Minecart = null; // the tongue drags you out of a cart
        if (UpdateMinecart(dt) || UpdatePhase())
        {
            ReelInHook(dt);
            return;
        }

        _liquid = LiquidAt(GlobalPosition + Vector3.Up * 0.9f);
        _swimming = _liquid != null;
        _climbing = !_swimming && OnRope();
        var stats = Entity.State.Effects.Stats;
        float speed = RunSpeed(stats) * stats.Multiplier(StatIds.MoveSpeed);
        if (_liquid != null) speed = Mathf.Min(speed * _liquid.MoveSpeed * (1f + stats.Bonus(StatIds.SwimSpeed)), _config.SwimSpeed * (1f + stats.Bonus(StatIds.SwimSpeed)));
        speed *= AirSpeedFactor();
        if (_climbing) speed *= 0.6f;

        bool blocked = UI.InputGate.MovementBlocked;
        var input = MoveInput(); // reversed while confused (Status)
        var wish = Rig.YawBasis * new Vector3(input.X, 0, input.Y);
        var v = Velocity;
        var horizontal = new Vector3(v.X, 0, v.Z);
        var target = wish * speed;
        bool onFloor = IsOnFloor();
        if (onFloor) RefillAirMovement();
        float accel = onFloor ? (wish.LengthSquared() > 0.01f ? _config.GroundAcceleration : _config.GroundFriction) : _config.AirAcceleration;
        if (_knockbackTime > 0)
        {
            _knockbackTime -= dt; // briefly lose control while knocked back
            accel *= 0.15f;
        }
        if (UpdateDash(dt, ref horizontal)) accel *= 0.08f; // a dash carries its own momentum
        horizontal = horizontal.MoveToward(target, accel * dt);
        TrackRunUp(dt, onFloor, wish, horizontal, stats);

        UpdateJump(dt, onFloor, ref v);
        v.X = horizontal.X;
        v.Z = horizontal.Z;
        if (UpdateGrapple(dt, ref v)) onFloor = false;
        if (UpdateTongue(ref v)) onFloor = false; // after all input, so it cannot be out-run (Status)
        Velocity = v;

        bool dropThrough = !blocked && Input.IsActionPressed("crouch");
        CollisionMask = ColliderManager.TerrainLayer | ColliderManager.ObjectLayer |
            (v.Y <= 0.5f && !dropThrough ? ColliderManager.PlatformLayer : 0u);

        var before = GlobalPosition;
        MoveAndSlide();
        if (IsOnFloor() && IsOnWall() && wish.LengthSquared() > 0.01f) TryStepUp(before, wish);
        TrackFall();
        UpdateSounds(dt);
        Sync();
    }

    /// <summary>Knockback from a hit: replaces horizontal momentum and pops the player up.</summary>
    public void ApplyKnockback(Vector3 impulse)
    {
        var v = Velocity;
        v.X = impulse.X;
        v.Z = impulse.Z;
        v.Y = Mathf.Max(v.Y, impulse.Y);
        Velocity = v;
        _knockbackTime = 0.25f;
    }

    private float _knockbackTime;

    private void UpdateJump(float dt, bool onFloor, ref Vector3 v)
    {
        _coyote = onFloor ? _config.CoyoteTime : _coyote - dt;
        bool blocked = UI.InputGate.MovementBlocked;
        _jumpBuffer = !blocked && Input.IsActionJustPressed("jump") ? _config.JumpBuffer : _jumpBuffer - dt;
        bool held = !blocked && Input.IsActionPressed("jump");
        float jumpScale = 1f + Entity.State.Effects.Stats.Bonus(StatIds.JumpHeight);

        if (UpdateClimb(ref v)) return;
        if (_liquid != null)
        {
            // Viscous liquids (honey, lava) are harder to swim up through.
            float up = _config.SwimUpSpeed * _liquid.SwimStrength;
            v.Y = held ? Mathf.MoveToward(v.Y, up, 20f * _liquid.SwimStrength * dt) : Mathf.MoveToward(v.Y, -_config.SwimGravity * 0.5f, _config.SwimGravity * dt);
            _peakY = GlobalPosition.Y;
            return;
        }
        if (_jumpBuffer > 0 && _coyote > 0)
        {
            v.Y = _config.JumpSpeed * Mathf.Sqrt(jumpScale);
            StartJumpApex();
            _jumpHold = _config.JumpHoldTime;
            _jumpBuffer = 0;
            _coyote = 0;
        }
        else if (!blocked && Input.IsActionJustPressed("jump") && !onFloor && _coyote <= 0 && TryAirJump(ref v))
        {
            _jumpBuffer = 0;
            _jumpHold = 0;
        }
        float gravity = _config.Gravity * (InSpace() ? SpaceGravity : 1f);
        if (_jumpHold > 0 && held && v.Y > 0)
        {
            gravity *= _config.JumpHoldGravity;
            _jumpHold -= dt;
        }
        else
        {
            _jumpHold = 0;
        }
        if (!onFloor || v.Y > 0) v.Y = Mathf.Max(v.Y - gravity * dt, -_config.MaxFallSpeed);
        if (!onFloor) UpdateFlight(dt, held, ref v);
    }

    /// <summary>Climbs a ledge up to <see cref="MovementConfig.StepHeight"/> when walking into it.</summary>
    private void TryStepUp(Vector3 before, Vector3 wish)
    {
        var up = new Vector3(0, _config.StepHeight, 0);
        var forward = wish.Normalized() * 0.25f;
        var start = new Transform3D(GlobalTransform.Basis, before);
        if (TestMove(start, up)) return;
        var raised = start.Translated(up);
        if (TestMove(raised, forward)) return;
        var stepped = raised.Translated(forward);
        var collision = new KinematicCollision3D();
        if (!TestMove(stepped, -up, collision)) return; // nothing to stand on: not a ledge
        GlobalPosition = stepped.Origin + collision.GetTravel();
        ApplyFloorSnap();
    }

    private void Sync()
    {
        var p = GlobalPosition;
        Entity.Position = new System.Numerics.Vector3(p.X, p.Y, p.Z);
        Entity.Velocity = new System.Numerics.Vector3(Velocity.X, Velocity.Y, Velocity.Z);
    }

    private Strata.Core.Content.Defs.LiquidDef? LiquidAt(Vector3 point)
    {
        var c = CellAt(point);
        return _runtime.Session.Content.LiquidOf(_runtime.Session.World.GetLiquid(c.X, c.Y, c.Z));
    }

    public static Int3 CellAt(Vector3 meters) =>
        new(Mathf.FloorToInt(meters.X / Units.CellSize), Mathf.FloorToInt(meters.Y / Units.CellSize), Mathf.FloorToInt(meters.Z / Units.CellSize));

    public static Vector3 ToGodot(System.Numerics.Vector3 v) => new(v.X, v.Y, v.Z);
}
