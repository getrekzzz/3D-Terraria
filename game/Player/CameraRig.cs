using Godot;
using Strata.Game.Settings;
using Strata.Game.World;

namespace Strata.Game.Player;

/// <summary>
/// Over-the-shoulder third-person camera and a first-person mode. Mouse look (or the controller's right stick with aim
/// assist, CameraRig.Pad.cs) drives yaw/pitch; the third-person body
/// faces the camera's <see cref="Yaw"/> (its chest is always on the crosshair, see PlayerAvatar.Motion), and the
/// crosshair ray runs from <see cref="ViewOrigin"/> along <see cref="Forward"/>. The camera is kept out of terrain every
/// rendered frame with a sphere cast from the head to the shoulder and on to the camera, so turning quickly can never
/// swing it through the ground (a physics-tick spring arm lags the view by up to a tick). Collisions pull it in at
/// once; it eases back out. <see cref="Framing"/> pulls it back and widens the view a little while a boss arrives
/// (UI.BossIntro); it never turns the view, so the player keeps control.
/// </summary>
public sealed partial class CameraRig : Node3D
{
    public const float EyeHeight = 1.6f;
    private const float ProbeRadius = 0.22f;
    private Vector3 _shoulder = new(0.55f, 0.1f, 0);

    /// <summary>Degrees of extra field of view per metre of <see cref="Framing"/>.</summary>
    private const float FramingFov = 1.6f;

    private CharacterBody3D _body = null!;
    private Node3D _pivot = null!;
    private SphereShape3D _probe = null!;
    private float _yaw;
    private float _pitch = -0.2f;
    private float _distance = 4.2f;
    private float _current = 4.2f;
    private bool _firstPerson;
    private float _fov = 72f;

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

    /// <summary>Third-person camera distance in metres for this session (the <c>camera distance</c> command; not saved).</summary>
    public float Distance
    {
        get => _distance;
        set => _distance = Mathf.Clamp(value, 1.5f, 9f);
    }

    public float Pitch => _pitch;

    /// <summary>How far right of the head the third-person camera sits, in metres (the <c>camera shoulder</c> command;
    /// 0 centres the player, for portraits).</summary>
    public float ShoulderOffset
    {
        get => _shoulder.X;
        set => _shoulder.X = Mathf.Clamp(value, -1f, 1f);
    }

    /// <summary>Extra metres the third-person camera pulls back (and a slightly wider view in either mode): a boss's arena framing.</summary>
    public float Framing { get; set; }

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
        _fov = Mathf.Clamp(s.FieldOfView, 50f, 110f);
        Camera.Fov = _fov;
        _distance = Mathf.Clamp(s.CameraDistance, 1.5f, 9f);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (UI.InputGate.HotkeysBlocked) return;
        if (@event is InputEventMouseMotion m)
        {
            // With a screen open the free pointer does not look around (a right drag on empty screen does, AddLook).
            if (!UI.InputGate.LookBlocked && Input.MouseMode == Input.MouseModeEnum.Captured) AddLook(m.Relative);
        }
        else if (@event.IsActionPressed("toggle_camera"))
        {
            FirstPerson = !FirstPerson;
        }
        else if (@event is InputEventMouseButton { Pressed: true, CtrlPressed: true } wheel && !_firstPerson && !UI.InputGate.PointerOverPanel(GetViewport()))
        {
            if (wheel.ButtonIndex == MouseButton.WheelUp) _distance = Mathf.Max(1.5f, _distance - 0.4f);
            if (wheel.ButtonIndex == MouseButton.WheelDown) _distance = Mathf.Min(9f, _distance + 0.4f);
            UserSettings.Current.CameraDistance = _distance;
            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>Turns the view by a mouse movement in pixels: the captured mouse, or a right-button drag on empty screen.</summary>
    public void AddLook(Vector2 relative)
    {
        var s = UserSettings.Current;
        float k = Sensitivity * Mathf.Max(0.05f, s.MouseSensitivity);
        _yaw -= relative.X * k;
        _pitch = Mathf.Clamp(_pitch - relative.Y * k * (s.InvertY ? -1f : 1f), -1.45f, 1.45f);
    }

    public override void _Process(double delta)
    {
        using var profile = Strata.Core.Diagnostics.Metrics.Frame.Time("gd.camera");
        PadLook((float)delta); // the controller's look stick and aim assist (CameraRig.Pad.cs)
        Rotation = new Vector3(_pitch, _yaw, 0);
        var shake = CameraShake.Sample(delta);
        Camera.HOffset = shake.X;
        Camera.VOffset = shake.Y;
        Camera.Rotation = new Vector3(0, 0, CameraShake.Roll);
        _pivot.Rotation = new Vector3(0, _firstPerson ? 0f : Orbit, 0);
        float fov = Mathf.Min(_fov + Framing * FramingFov, 120f);
        if (Camera.Fov != fov) Camera.Fov = fov;
        if (_firstPerson)
        {
            _pivot.Position = Vector3.Zero;
            Camera.Position = Vector3.Zero;
            _current = 0f;
            return;
        }
        // Head → shoulder, then shoulder → camera, each stopped a probe radius short of terrain.
        var head = GlobalPosition;
        var shoulderWorld = GlobalTransform * _shoulder;
        float shoulderT = SafeFraction(head, shoulderWorld);
        _pivot.Position = _shoulder * shoulderT;
        var pivotWorld = head + (shoulderWorld - head) * shoulderT;
        var back = GlobalTransform.Basis.Z.Normalized();
        float reach = _distance + Framing;
        float free = reach * SafeFraction(pivotWorld, pivotWorld + back * reach);
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
