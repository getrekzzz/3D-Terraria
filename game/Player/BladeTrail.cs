using System.Collections.Generic;
using Godot;

namespace Strata.Game.Player;

/// <summary>
/// The streak a held blade leaves as it strikes: a ribbon swept by the item from part way up to its tip, sampled each
/// frame once the skeleton is posed (so it starts right at the blade), smoothed between frames and fading back along
/// the path. The avatar sets <see cref="Strength"/> while a melee action strikes and tints it to the item.
/// </summary>
public sealed partial class BladeTrail : MeshInstance3D
{
    private struct Sample
    {
        public Vector3 Base, Tip;
        public float Time, Strength;
    }

    /// <summary>Curve points between two frames' samples, so a fast arc stays round.</summary>
    private const int Steps = 5;

    private readonly List<Sample> _samples = new();
    private readonly ImmediateMesh _mesh = new();
    private readonly ShaderMaterial _material = new() { Shader = GD.Load<Shader>("res://shaders/blade_trail.gdshader") };
    private Skeleton3D? _skeleton;
    private int _hand = -1;
    private Node3D? _item;
    private Vector3 _inner, _tip;
    private float _time;

    /// <summary>0..1 while the blade should leave a trail (0 stops adding to it; what is there fades).</summary>
    public float Strength { get; set; }

    /// <summary>Seconds the trail takes to fade behind the blade.</summary>
    public float Life { get; set; } = 0.12f;

    /// <summary>Scales the trail's brightness (dim in the dark, full in daylight).</summary>
    public float Brightness
    {
        set => _material.SetShaderParameter("brightness", value);
    }

    public void Initialize(Skeleton3D skeleton)
    {
        Name = "BladeTrail";
        TopLevel = true;
        GlobalTransform = Transform3D.Identity; // (made top level after joining the tree: it kept the avatar's place)
        Mesh = _mesh;
        MaterialOverride = _material;
        CastShadow = ShadowCastingSetting.Off;
        _skeleton = skeleton;
        _hand = skeleton.FindBone("hand.R");
        skeleton.SkeletonUpdated += OnPosed;
    }

    /// <summary>
    /// Follows a held item (a child of the right hand's attachment); the trail spans <paramref name="inner"/> to
    /// <paramref name="tip"/>, points in the item's own space. Null stops it.
    /// </summary>
    public void Track(Node3D? item, Vector3 inner, Vector3 tip, Color tint)
    {
        _item = item;
        _inner = inner;
        _tip = tip;
        _samples.Clear();
        _material.SetShaderParameter("tint", tint);
    }

    public override void _Process(double delta)
    {
        _time += (float)delta;
        Rebuild();
    }

    public override void _ExitTree()
    {
        if (_skeleton != null && IsInstanceValid(_skeleton)) _skeleton.SkeletonUpdated -= OnPosed;
    }

    private void OnPosed()
    {
        if (_skeleton == null || _hand < 0 || _item == null || !IsInstanceValid(_item) || Strength <= 0f) return;
        // The item's attachment follows the hand bone exactly; work from the bone so this frame's pose is used.
        var item = _skeleton.GlobalTransform * _skeleton.GetBoneGlobalPose(_hand) * _item.Transform;
        var sample = new Sample { Base = item * _inner, Tip = item * _tip, Time = _time, Strength = Strength };
        if (_samples.Count > 0 && _samples[^1].Tip.DistanceSquaredTo(sample.Tip) < 1e-6f) return;
        _samples.Add(sample);
        Rebuild();
    }

    private void Rebuild()
    {
        while (_samples.Count > 0 && _time - _samples[0].Time > Life) _samples.RemoveAt(0);
        _mesh.ClearSurfaces();
        if (_samples.Count < 2) return; // (an empty surface is an error)
        _mesh.SurfaceBegin(Mesh.PrimitiveType.TriangleStrip);
        for (int i = _samples.Count - 1; i > 0; i--)
        {
            Sample a = _samples[Mathf.Min(i + 1, _samples.Count - 1)], b = _samples[i], c = _samples[i - 1], d = _samples[Mathf.Max(i - 2, 0)];
            for (int k = 0; k < Steps; k++)
            {
                float u = k / (float)Steps;
                Emit(Spline(a.Base, b.Base, c.Base, d.Base, u), Spline(a.Tip, b.Tip, c.Tip, d.Tip, u),
                    Mathf.Lerp(b.Time, c.Time, u), Mathf.Lerp(b.Strength, c.Strength, u));
            }
        }
        var last = _samples[0];
        Emit(last.Base, last.Tip, last.Time, last.Strength);
        _mesh.SurfaceEnd();
    }

    private void Emit(Vector3 inner, Vector3 tip, float time, float strength)
    {
        float age = Mathf.Clamp((_time - time) / Life, 0f, 1f);
        float alpha = strength * Mathf.Pow(1f - age, 1.6f) * 0.85f;
        _mesh.SurfaceSetColor(new Color(1, 1, 1, alpha));
        _mesh.SurfaceSetUV(new Vector2(age, 0));
        _mesh.SurfaceAddVertex(inner);
        _mesh.SurfaceSetColor(new Color(1, 1, 1, alpha));
        _mesh.SurfaceSetUV(new Vector2(age, 1));
        _mesh.SurfaceAddVertex(tip);
    }

    /// <summary>Catmull-Rom between <paramref name="b"/> and <paramref name="c"/>.</summary>
    private static Vector3 Spline(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float u) =>
        0.5f * (2f * b + (c - a) * u + (2f * a - 5f * b + 4f * c - d) * u * u + (3f * b - a - 3f * c + d) * u * u * u);
}
