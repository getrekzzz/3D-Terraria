using Godot;

namespace Strata.Game.Debug;

/// <summary>Fly camera for inspecting the world (mouse look, WASD, Space/Ctrl up/down, Shift to boost).</summary>
public sealed partial class FreeCamera : Camera3D
{
    private float _yaw;
    private float _pitch;

    public float Speed { get; set; } = 12f;

    public override void _Ready()
    {
        Far = 1500f;
        Fov = 70f;
        var e = Rotation;
        _yaw = e.Y;
        _pitch = e.X;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!Current) return;
        if (@event is InputEventMouseMotion m && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            _yaw -= m.Relative.X * 0.0025f;
            _pitch = Mathf.Clamp(_pitch - m.Relative.Y * 0.0025f, -1.5f, 1.5f);
            Rotation = new Vector3(_pitch, _yaw, 0);
        }
    }

    public override void _Process(double delta)
    {
        if (!Current) return;
        var input = Input.GetVector("move_left", "move_right", "move_forward", "move_back");
        var dir = GlobalTransform.Basis * new Vector3(input.X, 0, input.Y);
        if (Input.IsActionPressed("jump")) dir += Vector3.Up;
        if (Input.IsActionPressed("crouch")) dir += Vector3.Down;
        float speed = Speed * (Input.IsActionPressed("sprint") ? 5f : 1f);
        GlobalPosition += dir * speed * (float)delta;
    }

    public void LookFrom(Vector3 position, Vector3 target)
    {
        GlobalPosition = position;
        LookAt(target, Vector3.Up);
        _yaw = Rotation.Y;
        _pitch = Rotation.X;
    }
}
