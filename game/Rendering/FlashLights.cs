using Godot;

namespace Strata.Game.Rendering;

/// <summary>
/// A small pool of short-lived lights for flashes (explosions, sparks, lightning-hot hits): <see cref="Flash"/> lights
/// one that fades out linearly; with every light busy the oldest is taken. The pool size is the graphics preset's
/// <c>flashLights</c> (0 on low: flashes are skipped). A separate pool rather than a DynamicLights provider, because those
/// are re-ranked a few times a second and could neither start a flash on time nor fade it.
/// </summary>
public sealed partial class FlashLights : Node3D
{
    private OmniLight3D[] _lights = System.Array.Empty<OmniLight3D>();
    private float[] _left = System.Array.Empty<float>(), _length = System.Array.Empty<float>(), _energy = System.Array.Empty<float>();

    public static FlashLights? Instance { get; private set; }

    public void Initialize(GraphicsSettings graphics)
    {
        Name = "FlashLights";
        Instance = this;
        Resize(graphics.Preset.FlashLights);
        graphics.Changed += p => Resize(p.FlashLights);
    }

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
    }

    public void Flash(Vector3 position, Color color, float energy, float range, float seconds)
    {
        if (_lights.Length == 0) return;
        int pick = 0;
        for (int i = 0; i < _lights.Length; i++)
        {
            if (_left[i] <= 0f)
            {
                pick = i;
                break;
            }
            if (_left[i] < _left[pick]) pick = i;
        }
        var l = _lights[pick];
        l.GlobalPosition = position;
        l.LightColor = color;
        l.OmniRange = range;
        l.LightEnergy = energy;
        l.Visible = true;
        _left[pick] = _length[pick] = Mathf.Max(seconds, 0.05f);
        _energy[pick] = energy;
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        for (int i = 0; i < _lights.Length; i++)
        {
            if (_left[i] <= 0f) continue;
            _left[i] -= dt;
            if (_left[i] <= 0f) _lights[i].Visible = false;
            else _lights[i].LightEnergy = _energy[i] * (_left[i] / _length[i]);
        }
    }

    private void Resize(int count)
    {
        foreach (var l in _lights) l.QueueFree();
        _lights = new OmniLight3D[count];
        _left = new float[count];
        _length = new float[count];
        _energy = new float[count];
        for (int i = 0; i < count; i++)
        {
            _lights[i] = new OmniLight3D { ShadowEnabled = false, Visible = false, OmniAttenuation = 1.4f };
            AddChild(_lights[i]);
        }
    }
}
