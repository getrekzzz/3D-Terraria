using System.Collections.Generic;
using Godot;
using Strata.Core.Content.Defs;

namespace Strata.Game.Rendering;

/// <summary>
/// Living flames (<c>shaders/flame.gdshader</c>): batches for placed torches and candles, and a single flame with
/// embers for a held torch. One material per flame colour; the quad is billboarded in the shader.
/// </summary>
public static class Flames
{
    /// <summary>Flames further than this are not drawn (the shader fades them out before).</summary>
    public const float Range = 60f;

    private static readonly Dictionary<string, ShaderMaterial> Materials = new();
    private static readonly Dictionary<string, QuadMesh> Quads = new();
    private static Shader? _shader;

    public static ShaderMaterial Material(string color)
    {
        if (Materials.TryGetValue(color, out var m)) return m;
        _shader ??= GD.Load<Shader>("res://shaders/flame.gdshader");
        m = new ShaderMaterial { Shader = _shader };
        m.SetShaderParameter("flame_color", Color.FromHtml(color));
        return Materials[color] = m;
    }

    private static QuadMesh Quad(string color)
    {
        if (!Quads.TryGetValue(color, out var quad)) Quads[color] = quad = new QuadMesh { Size = Vector2.One, Material = Material(color) };
        return quad;
    }

    /// <summary>The flame of the object an item places (a held torch burns like a placed one).</summary>
    public static FlameSpec? Of(ItemDef? item, Strata.Core.Content.ContentDatabase content) =>
        item?.PlaceObject is { } id && content.TileObjects.TryGet(id, out var obj) ? obj.Flame : null;

    /// <summary>Where a flame burns for an object placed with <paramref name="objectTransform"/>.</summary>
    public static Vector3 BaseOf(FlameSpec spec, Transform3D objectTransform) =>
        objectTransform * new Vector3(spec.At[0], spec.At[1], spec.At[2]);

    /// <summary>A batch of flames of one colour at world positions (bases) with per-flame phase.</summary>
    public static MultiMeshInstance3D Batch(FlameSpec spec, IReadOnlyList<Vector3> bases)
    {
        var mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseCustomData = true,
            Mesh = Quad(spec.Color),
            InstanceCount = bases.Count,
        };
        for (int i = 0; i < bases.Count; i++)
        {
            mm.SetInstanceTransform(i, new Transform3D(Basis.Identity, bases[i]));
            mm.SetInstanceCustomData(i, new Color(Phase(bases[i]), spec.Size, 0, 0));
        }
        return new MultiMeshInstance3D
        {
            Multimesh = mm,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            VisibilityRangeEnd = Range,
            // The billboard is built in the shader: keep the box generous so culling never clips a flame.
            CustomAabb = new Aabb(-Vector3.One, Vector3.One * 2f),
        };
    }

    /// <summary>One flame (a held torch) at the origin of the returned node, with rising embers.</summary>
    public static Node3D Single(FlameSpec spec, bool embers)
    {
        var root = new Node3D { Name = "Flame", Position = new Vector3(spec.At[0], spec.At[1], spec.At[2]) };
        var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseCustomData = true, Mesh = Quad(spec.Color), InstanceCount = 1 };
        mm.SetInstanceTransform(0, Transform3D.Identity);
        mm.SetInstanceCustomData(0, new Color(0.37f, spec.Size, 0, 0));
        root.AddChild(new MultiMeshInstance3D { Multimesh = mm, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, CustomAabb = new Aabb(-Vector3.One, Vector3.One * 2f) });
        if (embers) root.AddChild(Embers(spec));
        return root;
    }

    /// <summary>A few sparks drifting up from a flame (world space, so they trail behind a moving torch).</summary>
    public static CpuParticles3D Embers(FlameSpec spec, int amount = 5)
    {
        var dot = new GradientTexture2D
        {
            Gradient = new Gradient { Colors = new[] { new Color(1, 1, 1, 1), new Color(1, 1, 1, 0) }, Offsets = new[] { 0f, 1f } },
            Fill = GradientTexture2D.FillEnum.Radial, FillFrom = new Vector2(0.5f, 0.5f), FillTo = new Vector2(1f, 0.5f), Width = 32, Height = 32,
        };
        var material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
            VertexColorUseAsAlbedo = true,
            AlbedoTexture = dot,
            DisableReceiveShadows = true,
        };
        var ramp = new Gradient
        {
            Colors = new[] { new Color(2.2f, 1.2f, 0.4f, 1f), new Color(1.8f, 0.55f, 0.12f, 0.9f), new Color(0.6f, 0.1f, 0.03f, 0f) },
            Offsets = new[] { 0f, 0.45f, 1f },
        };
        return new CpuParticles3D
        {
            Name = "Embers",
            Amount = amount,
            Lifetime = 1.3,
            Randomness = 0.6f,
            LocalCoords = false,
            Mesh = new QuadMesh { Size = Vector2.One * 0.012f, Material = material },
            EmissionShape = CpuParticles3D.EmissionShapeEnum.Sphere,
            EmissionSphereRadius = spec.Size * 0.25f,
            Direction = Vector3.Up,
            Spread = 25f,
            Gravity = new Vector3(0, 0.9f, 0),
            InitialVelocityMin = 0.25f,
            InitialVelocityMax = 0.8f,
            DampingMin = 0.4f,
            DampingMax = 1.2f,
            ScaleAmountMin = 0.4f,
            ScaleAmountMax = 1.1f,
            ColorRamp = ramp,
            Position = new Vector3(0, spec.Size * 0.6f, 0),
        };
    }

    /// <summary>A stable per-position phase so neighbouring flames never flicker in step.</summary>
    private static float Phase(Vector3 p) => Mathf.PosMod(Mathf.Sin(p.X * 12.9898f + p.Y * 78.233f + p.Z * 37.719f) * 43758.547f, 1f);
}
