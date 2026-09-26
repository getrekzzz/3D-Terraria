using Godot;
using Strata.Core.Gameplay.Player;
using Strata.Game.Rendering;

namespace Strata.Game.Menu;

/// <summary>
/// A turntable of a character in its own little 3D world (character creation and the character list): the model for
/// the chosen body and hair style, recoloured to the chosen look, idling and slowly turning.
/// </summary>
public sealed partial class CharacterPreview : SubViewportContainer
{
    private static PropLibrary? _props;
    private SubViewport _viewport = null!;
    private Node3D _turntable = null!;
    private CreatureVisual? _visual;
    private string _model = "";

    public void Initialize(Vector2 size)
    {
        Stretch = true;
        CustomMinimumSize = size;
        _viewport = new SubViewport { OwnWorld3D = true, TransparentBg = true, Msaa3D = Viewport.Msaa.Msaa4X, Size = (Vector2I)size };
        AddChild(_viewport);
        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.ClearColor,
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(0.55f, 0.6f, 0.7f),
            AmbientLightEnergy = 0.8f,
            TonemapMode = Godot.Environment.ToneMapper.Agx,
        };
        _viewport.AddChild(new WorldEnvironment { Environment = env });
        var key = new DirectionalLight3D { LightEnergy = 1.6f, RotationDegrees = new Vector3(-35, 30, 0) };
        _viewport.AddChild(key);
        _viewport.AddChild(new DirectionalLight3D { LightEnergy = 0.5f, RotationDegrees = new Vector3(-10, 200, 0), LightColor = new Color(0.7f, 0.8f, 1f) });
        var camera = new Camera3D { Fov = 30f, Current = true };
        _viewport.AddChild(camera);
        camera.LookAtFromPosition(new Vector3(0, 1.05f, 4.4f), new Vector3(0, 0.95f, 0), Vector3.Up);
        _turntable = new Node3D();
        _viewport.AddChild(_turntable);
    }

    /// <summary>Shows a look (swaps the model when the body or hair style changed).</summary>
    public void Show(Appearance look)
    {
        _props ??= new PropLibrary();
        var model = $"assets/models/creatures/{look.ModelId}.glb";
        if (model != _model || _visual == null)
        {
            _visual?.QueueFree();
            _visual = null;
            _model = model;
            if (!ResourceLoader.Exists("res://" + model)) return;
            _visual = CreatureVisual.Create(model, _props);
            if (_visual == null) return;
            _turntable.AddChild(_visual);
            _visual.SetLodBias(8f);
            _visual.Play("idle");
        }
        _visual.SetAppearance(look);
    }

    public override void _Process(double delta)
    {
        if (_turntable != null) _turntable.RotateY((float)delta * 0.5f);
    }
}
