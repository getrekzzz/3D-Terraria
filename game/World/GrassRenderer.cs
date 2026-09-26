using System.Collections.Generic;
using Godot;
using Strata.Core.Common;
using Strata.Core.Meshing;
using Strata.Core.Presentation;
using Strata.Game.Rendering;

namespace Strata.Game.World;

/// <summary>
/// Draws the grass scattered by the mesh jobs (<see cref="ChunkMeshResult.Grass"/>): one MultiMesh of clumps (and
/// one of flowers) per chunk, created only while the chunk is within the preset's grass distance. Clumps arrive
/// shuffled, so drawing a prefix thins a chunk evenly: far chunks draw fewer, simpler clumps (the shader widens
/// them), and the blades shrink into the ground at the distance limit.
/// </summary>
public sealed partial class GrassRenderer : Node3D
{
    private const int FloatsPerInstance = 20;   // 3×4 transform, colour, custom data
    private const float ChunkMeters = ChunkMath.Size * Units.CellSize;
    private const float TrampleRadius = 0.9f;

    private sealed class ChunkGrass
    {
        public GrassClump[] Clumps = System.Array.Empty<GrassClump>();
        public int Flowers;
        public Rid Instance, Multimesh, FlowerInstance, FlowerMultimesh;
        public int Visible = -1;
        public bool Near;
        public bool Live => Multimesh.IsValid;
    }

    private readonly Dictionary<Int3, ChunkGrass> _chunks = new();
    private GrassConfig _config = null!;
    private GraphicsSettings _graphics = null!;
    private Vector3[] _kindColors = System.Array.Empty<Vector3>();
    private float[] _kindGlow = System.Array.Empty<float>();
    private Color[][] _flowerColors = System.Array.Empty<Color[]>();
    private ArrayMesh _near = null!, _far = null!, _flower = null!;
    private ShaderMaterial _material = null!, _flowerMaterial = null!;

    public int LiveChunks { get; private set; }
    public long VisibleClumps { get; private set; }

    public void Initialize(GrassConfig config, GraphicsSettings graphics)
    {
        _config = config;
        _graphics = graphics;
        var kinds = config.Ordered;
        _kindColors = new Vector3[kinds.Count];
        _kindGlow = new float[kinds.Count];
        _flowerColors = new Color[kinds.Count][];
        for (int k = 0; k < kinds.Count; k++)
        {
            var look = kinds[k].Value;
            var c = GrassKind.Rgb(look.Color);
            var linear = new Color(c.X, c.Y, c.Z).SrgbToLinear();
            _kindColors[k] = new Vector3(linear.R, linear.G, linear.B);
            _kindGlow[k] = look.Glow;
            _flowerColors[k] = System.Array.ConvertAll(look.FlowerColors, h =>
            {
                var f = GrassKind.Rgb(h);
                return new Color(f.X, f.Y, f.Z).SrgbToLinear();
            });
        }
        _near = GrassMeshes.Clump(9, 4, 11);
        _far = GrassMeshes.Clump(5, 2, 11);
        _flower = GrassMeshes.Flower(5);
        var shader = GD.Load<Shader>("res://shaders/grass.gdshader");
        _material = new ShaderMaterial { Shader = shader };
        _flowerMaterial = new ShaderMaterial { Shader = shader };
        _flowerMaterial.SetShaderParameter("flower", true);
    }

    /// <summary>Replaces a chunk's grass (called with every uploaded mesh).</summary>
    public void Set(Int3 coord, List<GrassClump> clumps)
    {
        Remove(coord);
        if (clumps.Count == 0) return;
        var chunk = new ChunkGrass { Clumps = clumps.ToArray() };
        foreach (var c in chunk.Clumps)
            if (c.Flower != 0) chunk.Flowers++;
        _chunks[coord] = chunk;
    }

    public void Remove(Int3 coord)
    {
        if (!_chunks.Remove(coord, out var chunk)) return;
        Release(chunk);
    }

    /// <summary>Creates, thins and frees chunk grass around the camera; parts the grass around the player.</summary>
    public void Update(Vector3 camera, Vector3 playerFeet)
    {
        RenderingServer.GlobalShaderParameterSet("strata_trample", new Vector4(playerFeet.X, playerFeet.Y, playerFeet.Z, TrampleRadius));
        var preset = _graphics.Preset;
        float distance = preset.GrassDistance;
        float density = preset.GrassDensity;
        _material.SetShaderParameter("fade_start", distance * 0.72f);
        _material.SetShaderParameter("fade_end", distance);
        _flowerMaterial.SetShaderParameter("fade_start", distance * 0.5f);
        _flowerMaterial.SetShaderParameter("fade_end", distance * 0.7f);
        int live = 0;
        long visible = 0;
        foreach (var (coord, chunk) in _chunks)
        {
            float d = DistanceToChunk(coord, camera);
            if (density <= 0f || d > distance)
            {
                Release(chunk);
                continue;
            }
            if (!chunk.Live) Create(coord, chunk);
            float thin = 1f - 0.65f * Mathf.SmoothStep(distance * 0.3f, distance, d);
            int count = Mathf.Clamp(Mathf.CeilToInt(chunk.Clumps.Length * density * thin), 0, chunk.Clumps.Length);
            bool near = d < distance * 0.35f;
            if (count != chunk.Visible)
            {
                RenderingServer.MultimeshSetVisibleInstances(chunk.Multimesh, count);
                chunk.Visible = count;
            }
            if (near != chunk.Near)
            {
                RenderingServer.MultimeshSetMesh(chunk.Multimesh, (near ? _near : _far).GetRid());
                chunk.Near = near;
            }
            live++;
            visible += count;
        }
        LiveChunks = live;
        VisibleClumps = visible;
    }

    private static float DistanceToChunk(Int3 coord, Vector3 p)
    {
        var min = new Vector3(coord.X, coord.Y, coord.Z) * ChunkMeters;
        var max = min + Vector3.One * ChunkMeters;
        return p.Clamp(min, max).DistanceTo(p);
    }

    private void Create(Int3 coord, ChunkGrass chunk)
    {
        var scenario = GetWorld3D().Scenario;
        var transform = new Transform3D(Basis.Identity, new Vector3(coord.X, coord.Y, coord.Z) * ChunkMeters);
        var bounds = new Aabb(new Vector3(-1f, -1f, -1f), Vector3.One * (ChunkMeters + 2f));
        var clumps = new float[chunk.Clumps.Length * FloatsPerInstance];
        var flowers = new float[chunk.Flowers * FloatsPerInstance];
        int f = 0;
        for (int i = 0; i < chunk.Clumps.Length; i++)
        {
            var c = chunk.Clumps[i];
            var basis = ClumpBasis(c);
            var root = ToGodot(c.Position) - basis.Y.Normalized() * 0.03f;
            float jitter = 0.86f + 0.28f * (c.Shade / 255f);
            var tint = _kindColors[c.Kind] * new Vector3(jitter, jitter * (0.97f + 0.06f * ((c.Shade * 7) & 255) / 255f), jitter * 0.95f);
            var light = Light(c.Light);
            Write(clumps, i, basis, root, new Color(tint.X, tint.Y, tint.Z, _kindGlow[c.Kind]), light);
            if (c.Flower == 0) continue;
            var flowerBasis = basis.Scaled(new Vector3(1f, 1.15f, 1f));
            Write(flowers, f++, flowerBasis, root, _flowerColors[c.Kind][c.Flower - 1], light);
        }
        chunk.Multimesh = CreateMultimesh(_near, clumps, chunk.Clumps.Length, bounds);
        chunk.Instance = CreateInstance(chunk.Multimesh, _material, scenario, transform);
        chunk.Near = true;
        chunk.Visible = chunk.Clumps.Length;
        if (chunk.Flowers > 0)
        {
            chunk.FlowerMultimesh = CreateMultimesh(_flower, flowers, chunk.Flowers, bounds);
            chunk.FlowerInstance = CreateInstance(chunk.FlowerMultimesh, _flowerMaterial, scenario, transform);
        }
    }

    /// <summary>Clump orientation: mostly upright (grass grows toward the light), yawed, scaled to its height.</summary>
    private static Basis ClumpBasis(GrassClump c)
    {
        var up = ToGodot(c.Normal).Lerp(Vector3.Up, 0.7f).Normalized();
        var yawed = new Basis(Vector3.Up, c.Yaw);
        var tilt = new Basis(new Quaternion(Vector3.Up, up));
        float spread = 0.75f + 0.7f * c.Height;
        return (tilt * yawed).Scaled(new Vector3(spread, c.Height, spread));
    }

    private static Vector3 ToGodot(System.Numerics.Vector3 v) => new(v.X, v.Y, v.Z);

    private static Color Light(uint packed) =>
        new((packed & 255) / 255f, ((packed >> 8) & 255) / 255f, ((packed >> 16) & 255) / 255f, (packed >> 24) / 255f);

    private static void Write(float[] buffer, int index, Basis b, Vector3 origin, Color color, Color custom)
    {
        int o = index * FloatsPerInstance;
        buffer[o + 0] = b.X.X; buffer[o + 1] = b.Y.X; buffer[o + 2] = b.Z.X; buffer[o + 3] = origin.X;
        buffer[o + 4] = b.X.Y; buffer[o + 5] = b.Y.Y; buffer[o + 6] = b.Z.Y; buffer[o + 7] = origin.Y;
        buffer[o + 8] = b.X.Z; buffer[o + 9] = b.Y.Z; buffer[o + 10] = b.Z.Z; buffer[o + 11] = origin.Z;
        buffer[o + 12] = color.R; buffer[o + 13] = color.G; buffer[o + 14] = color.B; buffer[o + 15] = color.A;
        buffer[o + 16] = custom.R; buffer[o + 17] = custom.G; buffer[o + 18] = custom.B; buffer[o + 19] = custom.A;
    }

    private static Rid CreateMultimesh(Mesh mesh, float[] buffer, int count, Aabb bounds)
    {
        var mm = RenderingServer.MultimeshCreate();
        RenderingServer.MultimeshAllocateData(mm, count, RenderingServer.MultimeshTransformFormat.Transform3D, true, true);
        RenderingServer.MultimeshSetMesh(mm, mesh.GetRid());
        RenderingServer.MultimeshSetCustomAabb(mm, bounds);
        RenderingServer.MultimeshSetBuffer(mm, buffer);
        return mm;
    }

    private static Rid CreateInstance(Rid multimesh, Material material, Rid scenario, Transform3D transform)
    {
        var instance = RenderingServer.InstanceCreate2(multimesh, scenario);
        RenderingServer.InstanceSetTransform(instance, transform);
        RenderingServer.InstanceGeometrySetMaterialOverride(instance, material.GetRid());
        RenderingServer.InstanceGeometrySetCastShadowsSetting(instance, RenderingServer.ShadowCastingSetting.Off);
        return instance;
    }

    private static void Release(ChunkGrass chunk)
    {
        if (!chunk.Live) return;
        RenderingServer.FreeRid(chunk.Instance);
        RenderingServer.FreeRid(chunk.Multimesh);
        if (chunk.FlowerMultimesh.IsValid)
        {
            RenderingServer.FreeRid(chunk.FlowerInstance);
            RenderingServer.FreeRid(chunk.FlowerMultimesh);
        }
        chunk.Instance = chunk.Multimesh = chunk.FlowerInstance = chunk.FlowerMultimesh = default;
        chunk.Visible = -1;
    }

    public override void _ExitTree()
    {
        foreach (var chunk in _chunks.Values) Release(chunk);
        _chunks.Clear();
    }
}
