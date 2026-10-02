using System.Collections.Generic;
using Godot;

namespace Strata.Game.Rendering;

/// <summary>
/// One-time pipeline warm-up: once the first preloaded assets are in (or after 2 s), every distinct material the world
/// uses is drawn for two frames on a tiny (0.01 scale) instance 1 m in front of the camera, as a plain mesh and as a
/// MultiMesh, so the driver compiles its pipelines (colour and shadow passes) before the first night, boss or torch
/// needs them. Covers the prop library's materials (props, clear props, leaf cards, characters), the flame, the lit
/// sprite and the projectile glow; the skinned character pipeline is already warm from the player's own body. Then the
/// instances are freed.
/// </summary>
public sealed partial class ShaderWarmup : Node3D
{
    private const double MaxWait = 2.0;
    private const int Frames = 2;
    private PropLibrary _props = null!;
    private double _elapsed;
    private int _framesShown = -1;

    /// <summary>Distinct materials drawn (probe <c>render.warmed</c>); 0 until the warm-up ran.</summary>
    public static int Warmed { get; private set; }

    public void Initialize(PropLibrary props)
    {
        _props = props;
        Name = "ShaderWarmup";
    }

    public override void _Process(double delta)
    {
        _elapsed += delta;
        if (_framesShown < 0)
        {
            if (AssetCache.PendingCount > 0 && _elapsed < MaxWait) return;
            if (GetViewport().GetCamera3D() is not { } camera)
            {
                if (_elapsed > MaxWait * 5) QueueFree(); // a world without a 3D camera: nothing to warm
                return;
            }
            Build(camera);
            _framesShown = 0;
            return;
        }
        if (++_framesShown >= Frames) QueueFree();
    }

    private void Build(Camera3D camera)
    {
        using var profile = Strata.Core.Diagnostics.Metrics.Frame.Time("gd.shader_warmup");
        TopLevel = true;
        var eye = camera.GlobalTransform;
        GlobalTransform = new Transform3D(eye.Basis.Scaled(Vector3.One * 0.01f), eye.Origin - eye.Basis.Z * 1f);
        var materials = new List<Material>();
        foreach (var m in _props.Materials) materials.Add(m);
        materials.Add(Flames.Material("#ffa040"));
        materials.Add(new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/sprite_lit.gdshader") });
        materials.Add(Entities.ProjectileRenderer.Glow(Colors.White, 3f));
        var box = new BoxMesh { Size = Vector3.One };
        foreach (var material in materials)
        {
            AddChild(new MeshInstance3D { Mesh = box, MaterialOverride = material });
            var multi = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                UseCustomData = true,
                Mesh = box,
                InstanceCount = 1,
            };
            multi.SetInstanceTransform(0, Transform3D.Identity);
            AddChild(new MultiMeshInstance3D { Multimesh = multi, MaterialOverride = material });
        }
        Warmed = materials.Count;
    }
}
