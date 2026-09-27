using System.Collections.Generic;
using System.Linq;
using Godot;
using Strata.Core.Common;

namespace Strata.Game.Rendering;

/// <summary>
/// What makes lava feel hot beyond its shader: embers drifting up from the surfaces near the camera, and a flickering
/// orange heat light over each lava-filled chunk (offered to <see cref="DynamicLights"/>). Lava surface cells come
/// from the chunk meshes (<see cref="Strata.Core.Meshing.ChunkMeshResult.LavaTops"/>).
/// </summary>
public sealed partial class LavaEffects : Node3D
{
    private const int Emitters = 6;
    private const float EmberRange = 26f;

    private readonly Dictionary<Int3, Int3[]> _tops = new();
    private readonly List<CpuParticles3D> _emitters = new();
    private readonly RandomNumberGenerator _rng = new();
    private double _reseed;

    public void Initialize(World.WorldRuntime runtime)
    {
        Name = "LavaEffects";
        runtime.Streamer.MeshReady += r =>
        {
            if (r.LavaTops.Count == 0) _tops.Remove(r.Coord);
            else _tops[r.Coord] = r.LavaTops.ToArray();
        };
        runtime.Streamer.ChunkRemoved += c => _tops.Remove(c);
        for (int i = 0; i < Emitters; i++)
        {
            var e = Embers();
            AddChild(e);
            _emitters.Add(e);
        }
    }

    /// <summary>Heat lights: one per lava-filled chunk, over the middle of its open lava.</summary>
    public IEnumerable<DynamicLights.Source> HeatLights(Vector3 eye, float radius)
    {
        foreach (var (chunk, tops) in _tops)
        {
            if (tops.Length < 3) continue;
            var sum = Vector3.Zero;
            foreach (var c in tops) sum += Center(c);
            var at = sum / tops.Length + Vector3.Up * 0.9f;
            if (at.DistanceTo(eye) > radius) continue;
            float size = Mathf.Clamp(tops.Length / 40f, 0.4f, 1f);
            yield return new DynamicLights.Source(at, new Color(1f, 0.42f, 0.08f), 0.9f * size, DynamicLights.Kind.Lava,
                6f + 6f * size, 1.3f * size, DynamicLights.Seed(at));
        }
    }

    public override void _Process(double delta)
    {
        using var profile = Strata.Core.Diagnostics.Metrics.Frame.Time("gd.lava_effects");
        _reseed -= delta;
        if (_reseed > 0) return;
        _reseed = 1.2;
        var camera = GetViewport()?.GetCamera3D();
        if (camera == null) return;
        var eye = camera.GlobalPosition;
        var near = new List<Vector3>();
        foreach (var tops in _tops.Values)
            foreach (var c in tops)
            {
                var p = Center(c);
                if (p.DistanceSquaredTo(eye) < EmberRange * EmberRange) near.Add(p);
            }
        // Move one emitter per reseed to a random nearby lava cell, so the embers wander over the pools.
        foreach (var e in _emitters)
        {
            if (near.Count == 0)
            {
                e.Emitting = false;
                continue;
            }
            if (e.Emitting && _rng.Randf() > 0.3f && e.GlobalPosition.DistanceTo(eye) < EmberRange) continue;
            e.GlobalPosition = near[_rng.RandiRange(0, near.Count - 1)] + Vector3.Up * 0.1f;
            e.Emitting = true;
        }
    }

    private static Vector3 Center(Int3 cell) => new Vector3(cell.X + 0.5f, cell.Y + 0.85f, cell.Z + 0.5f) * Units.CellSize;

    private static CpuParticles3D Embers()
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
        };
        var ramp = new Gradient
        {
            Colors = new[] { new Color(4f, 2.2f, 0.7f, 1f), new Color(2.6f, 0.7f, 0.12f, 0.9f), new Color(0.6f, 0.08f, 0.02f, 0f) },
            Offsets = new[] { 0f, 0.4f, 1f },
        };
        return new CpuParticles3D
        {
            Emitting = false,
            Amount = 14,
            Lifetime = 2.6,
            Randomness = 0.7f,
            LocalCoords = false,
            Mesh = new QuadMesh { Size = Vector2.One * 0.05f, Material = material },
            EmissionShape = CpuParticles3D.EmissionShapeEnum.Box,
            EmissionBoxExtents = new Vector3(1.6f, 0.05f, 1.6f),
            Direction = Vector3.Up,
            Spread = 30f,
            Gravity = new Vector3(0.15f, 0.55f, 0.1f),
            InitialVelocityMin = 0.4f,
            InitialVelocityMax = 1.6f,
            DampingMin = 0.2f,
            DampingMax = 0.7f,
            ScaleAmountMin = 0.35f,
            ScaleAmountMax = 1.2f,
            ColorRamp = ramp,
            VisibilityAabb = new Aabb(new Vector3(-3, -1, -3), new Vector3(6, 6, 6)),
        };
    }
}
