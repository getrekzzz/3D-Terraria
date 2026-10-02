using Godot;
using Strata.Game.Settings;
using Strata.Game.World;

namespace Strata.Game.Player;

/// <summary>
/// Over-the-shoulder third-person camera and a first-person mode. Mouse look drives yaw/pitch; the third-person body
/// faces the camera's <see cref="Yaw"/> (its chest is always on the crosshair, see PlayerAvatar.Motion), and the
/// crosshair ray runs from <see cref="ViewOrigin"/> along <see cref="Forward"/>. The camera is kept out of terrain every
/// rendered frame with a sphere cast from the head to the shoulder and on to the camera, so turning quickly can never
/// swing it through the ground (a physics-tick spring arm lags the view by up to a tick). Collisions pull it in at
/// once; it eases back out.
/// </summary>
public sealed partial class CameraRig : Node3D
{
    private const float EyeHeight = 1.6f;
    private const float ProbeRadius = 0.22f;
    private static readonly Vector3 Shoulder = new(0.55f, 0.1f, 0);

    private CharacterBody3D _body = null!;
    private Node3D _pivot = null!;
    private SphereShape3D _probe = null!;
    private float _yaw;
    private float _pitch = -0.2f;
    private float _distance = 4.2f;
    private float _current = 4.2f;
    private bool _firstPerson;

    public Camera3D Camera { get; private set; } = null!;

    public bool FirstPerson
    {
        get => _firstPerson;
        set => _firstPerson = value;
    }

    /// <summary>Radians per pixel of mouse motion at sensitivity 1.</summary>
    public float Sensitivity { get; set; } = 0.0025f;

    /// <summary>Rotation containing only the yaw (for movement input).</summary>
    public Basis YawBasis => new(Vector3.Up, _yaw);

    /// <summary>World-space view direction.</summary>
    public Vector3 Forward => -GlobalTransform.Basis.Z;

    /// <summary>Swings the third-person camera round the player (radians) without changing the aim (screenshots).</summary>
    public float Orbit { get; set; }

    public float Pitch => _pitch;

    /// <summary>The view's yaw (radians, Godot convention: 0 looks along −Z, + turns left): where the body faces.</summary>
    public float Yaw => Mathf.Wrap(_yaw, -Mathf.Pi, Mathf.Pi);

    /// <summary>
    /// Where the crosshair ray starts: the camera as placed this frame, but un-swung by <see cref="Orbit"/>, so orbiting
    /// for a screenshot changes neither the target nor the aim. The eye in first person.
    /// </summary>
    public Vector3 ViewOrigin => GlobalTransform * (_pivot.Position + new Vector3(0, 0, _current));

    public void Initialize(CharacterBody3D body)
    {
        _body = body;
        TopLevel = false;
        Position = new Vector3(0, EyeHeight, 0);
        _pivot = new Node3D { Name = "Pivot" };
        AddChild(_pivot);
        Camera = new Camera3D { Name = "Camera", Fov = 72f, Far = 1500f, Near = 0.05f };
        _pivot.AddChild(Camera);
        _probe = new SphereShape3D { Radius = ProbeRadius };
        _firstPerson = UserSettings.Current.FirstPerson;
        ApplySettings(UserSettings.Current);
        UserSettings.Current.Changed += ApplySettings;
    }

    public override void _ExitTree() => UserSettings.Current.Changed -= ApplySettings;

    private void ApplySettings(UserSettings s)
    {
        Camera.Fov = Mathf.Clamp(s.FieldOfView, 50f, 110f);
        _distance = Mathf.Clamp(s.CameraDistance, 1.5f, 9f);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (UI.InputGate.GameplayBlocked) return;
        var s = UserSettings.Current;
        if (@event is InputEventMouseMotion m && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            float k = Sensitivity * Mathf.Max(0.05f, s.MouseSensitivity);
            _yaw -= m.Relative.X * k;
            _pitch = Mathf.Clamp(_pitch - m.Relative.Y * k * (s.InvertY ? -1f : 1f), -1.45f, 1.45f);
        }
        else if (@event.IsActionPressed("toggle_camera"))
        {
            FirstPerson = !FirstPerson;
        }
        else if (@event is InputEventMouseButton { Pressed: true, CtrlPressed: true } wheel && !_firstPerson)
        {
            if (wheel.ButtonIndex == MouseButton.WheelUp) _distance = Mathf.Max(1.5f, _distance - 0.4f);
            if (wheel.ButtonIndex == MouseButton.WheelDown) _distance = Mathf.Min(9f, _distance + 0.4f);
            s.CameraDistance = _distance;
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _Process(double delta)
    {
        using var profile = Strata.Core.Diagnostics.Metrics.Frame.Time("gd.camera");
        Rotation = new Vector3(_pitch, _yaw, 0);
        var shake = CameraShake.Sample(delta);
        Camera.HOffset = shake.X;
        Camera.VOffset = shake.Y;
        Camera.Rotation = new Vector3(0, 0, CameraShake.Roll);
        _pivot.Rotation = new Vector3(0, _firstPerson ? 0f : Orbit, 0);
        if (_firstPerson)
        {
            _pivot.Position = Vector3.Zero;
            Camera.Position = Vector3.Zero;
            _current = 0f;
            return;
        }
        // Head → shoulder, then shoulder → camera, each stopped a probe radius short of terrain.
        var head = GlobalPosition;
        var shoulderWorld = GlobalTransform * Shoulder;
        float shoulderT = SafeFraction(head, shoulderWorld);
        _pivot.Position = Shoulder * shoulderT;
        var pivotWorld = head + (shoulderWorld - head) * shoulderT;
        var back = GlobalTransform.Basis.Z.Normalized();
        float free = _distance * SafeFraction(pivotWorld, pivotWorld + back * _distance);
        // Snap in immediately (never inside terrain), ease back out so the view does not pump.
        _current = free < _current ? free : Mathf.MoveToward(_current, free, (float)delta * 6f);
        Camera.Position = new Vector3(0, 0, _current);
    }

    /// <summary>Fraction (0–1) of the way from <paramref name="from"/> to <paramref name="to"/> the probe sphere can travel.</summary>
    private float SafeFraction(Vector3 from, Vector3 to)
    {
        var space = GetWorld3D()?.DirectSpaceState;
        if (space == null || from.DistanceSquaredTo(to) < 1e-6f) return 1f;
        var query = new PhysicsShapeQueryParameters3D
        {
            Shape = _probe,
            Transform = new Transform3D(Basis.Identity, from),
            Motion = to - from,
            CollisionMask = ColliderManager.TerrainLayer | ColliderManager.ObjectLayer,
            Exclude = new Godot.Collections.Array<Rid> { _body.GetRid() },
        };
        var result = space.CastMotion(query);
        return result.Length >= 1 ? Mathf.Clamp(result[0], 0f, 1f) : 1f;
    }

    /// <summary>Points the view at a world position (spawn framing, screenshots).</summary>
    public void LookToward(Vector3 target)
    {
        var d = target - GlobalPosition;
        _yaw = Mathf.Atan2(-d.X, -d.Z);
        _pitch = Mathf.Clamp(Mathf.Atan2(d.Y, new Vector2(d.X, d.Z).Length()), -1.45f, 1.45f);
        Rotation = new Vector3(_pitch, _yaw, 0);
    }
}
