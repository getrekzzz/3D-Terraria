using Godot;
using Strata.Game.World;

namespace Strata.Game.Player;

/// <summary>
/// Over-the-shoulder third-person camera with terrain collision (spring arm) and a first-person mode.
/// Mouse look drives yaw/pitch; the player body does not rotate with the camera (the model faces the aim).
/// </summary>
public sealed partial class CameraRig : Node3D
{
    private const float EyeHeight = 1.6f;
    private SpringArm3D _arm = null!;
    private float _yaw;
    private float _pitch = -0.2f;
    private float _distance = 4.2f;
    private bool _firstPerson;

    public Camera3D Camera { get; private set; } = null!;

    public bool FirstPerson
    {
        get => _firstPerson;
        set
        {
            _firstPerson = value;
            ApplyMode();
        }
    }

    public float Sensitivity { get; set; } = 0.0025f;

    /// <summary>Rotation containing only the yaw (for movement input).</summary>
    public Basis YawBasis => new(Vector3.Up, _yaw);

    /// <summary>World-space view direction.</summary>
    public Vector3 Forward => -Camera.GlobalTransform.Basis.Z;

    public void Initialize(CharacterBody3D body)
    {
        TopLevel = false;
        Position = new Vector3(0, EyeHeight, 0);
        _arm = new SpringArm3D { Name = "Arm", CollisionMask = ColliderManager.TerrainLayer | ColliderManager.ObjectLayer, Margin = 0.2f };
        _arm.Shape = new SphereShape3D { Radius = 0.25f };
        _arm.AddExcludedObject(body.GetRid());
        AddChild(_arm);
        Camera = new Camera3D { Name = "Camera", Fov = 72f, Far = 1500f, Near = 0.05f };
        _arm.AddChild(Camera);
        ApplyMode();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (UI.InputGate.GameplayBlocked) return;
        if (@event is InputEventMouseMotion m && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            _yaw -= m.Relative.X * Sensitivity;
            _pitch = Mathf.Clamp(_pitch - m.Relative.Y * Sensitivity, -1.45f, 1.45f);
        }
        else if (@event.IsActionPressed("toggle_camera"))
        {
            FirstPerson = !FirstPerson;
        }
        else if (@event is InputEventMouseButton { Pressed: true, CtrlPressed: true } wheel && !_firstPerson)
        {
            if (wheel.ButtonIndex == MouseButton.WheelUp) _distance = Mathf.Max(1.5f, _distance - 0.4f);
            if (wheel.ButtonIndex == MouseButton.WheelDown) _distance = Mathf.Min(9f, _distance + 0.4f);
            ApplyMode();
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _Process(double delta)
    {
        Rotation = new Vector3(_pitch, _yaw, 0);
    }

    /// <summary>Points the view at a world position (spawn framing, screenshots).</summary>
    public void LookToward(Vector3 target)
    {
        var d = target - GlobalPosition;
        _yaw = Mathf.Atan2(-d.X, -d.Z);
        _pitch = Mathf.Clamp(Mathf.Atan2(d.Y, new Vector2(d.X, d.Z).Length()), -1.45f, 1.45f);
        Rotation = new Vector3(_pitch, _yaw, 0);
    }

    private void ApplyMode()
    {
        if (_arm == null) return;
        _arm.SpringLength = _firstPerson ? 0f : _distance;
        _arm.Position = _firstPerson ? Vector3.Zero : new Vector3(0.55f, 0.1f, 0);
    }
}
