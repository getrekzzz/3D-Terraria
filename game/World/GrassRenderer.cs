using System.Collections.Generic;
using Godot;
using Strata.Core.Common;
using Strata.Core.Meshing;
using Strata.Core.Presentation;
using Strata.Game.Rendering;

namespace Strata.Game.World;

/// <summary>
/// Draws the grass scattered by the mesh jobs (<see cref="ChunkMeshResult.Grass"/>): one MultiMesh of clumps (and
/// one of flowers) per chunk, created only while the chunk is within the preset's grass distance and released
/// <see cref="ReleaseMargin"/> beyond it (no churn at the edge). Clumps arrive sorted by their hashed rank, so drawing
/// a prefix thins a chunk evenly, and a clump's place in or out of the prefix never depends on the rest of the chunk
/// (an edit only changes the grass it touches): far chunks draw fewer, simpler clumps (the shader widens them), and
/// the blades shrink into the ground at the distance limit. A remesh with the same grass does nothing, one that only
/// relit it rewrites the buffer in place; builds run nearest first, at most
/// <see cref="GraphicsPreset.GrassBuildsPerFrame"/> a frame within the frame's grass budget, and the old grass stays
/// visible until its rebuild. Live grass whose shape changed (an edit) is rebuilt at once: its terrain has already
/// switched, since grass is set when the terrain mesh uploads.
/// </summary>
public sealed partial class GrassRenderer : Node3D
{
    private const float ChunkMeters = ChunkMath.Size * Units.CellSize;
    private const float TrampleRadius = 0.9f;

    /// <summary>Live grass is released only this far (m) beyond the grass distance.</summary>
    private const float ReleaseMargin = 8f;

    private sealed class ChunkGrass
    {
        /// <summary>The clumps (first <see cref="Count"/>; the array is reused across remeshes).</summary>
        public GrassClump[] Clumps = System.Array.Empty<GrassClump>();
        public int Count, Flowers;
        public ulong Shape, Light;

        /// <summary>The clumps changed since the live buffers were written.</summary>
        public bool NeedsBuild = true;

        /// <summary>The live clumps' shape changed (not only their light) since the live buffers were written.</summary>
        public bool Reshaped;
        public Rid Instance, Multimesh, FlowerInstance, FlowerMultimesh;

        /// <summary>Instances allocated in the live multimeshes.</summary>
        public int Allocated, FlowersAllocated;

        /// <summary>Ranks of the clumps in the live buffer (first <see cref="Allocated"/>), for the LOD prefix.</summary>
        public float[] LiveRanks = System.Array.Empty<float>();
        public int Visible = -1;
        public bool Near;
        public bool Live => Multimesh.IsValid;
    }

    private readonly Dictionary<Int3, ChunkGrass> _chunks = new();
    private readonly List<(float Distance, Int3 Coord)> _builds = new();
    private GrassConfig _config = null!;
    private GraphicsSettings _graphics = null!;
    private Vector3[] _kindColors = System.Array.Empty<Vector3>();
    private float[] _kindGlow = System.Array.Empty<float>();
    private Color[][] _flowerColors = System.Array.Empty<Color[]>();
    private ArrayMesh _near = null!, _far = null!, _flower = null!;
    private ShaderMaterial _material = null!, _flowerMaterial = null!;
    private bool _fadeDirty = true;

    public int LiveChunks { get; private set; }
    public long VisibleClumps { get; private set; }

    /// <summary>Chunk grass buffers built (created or rewritten) since the last <see cref="ResetCounters"/>.</summary>
    public int Builds { get; private set; }

    /// <summary>Chunks in range waiting for a build after the last <see cref="Update"/>.</summary>
    public int PendingBuilds { get; private set; }

    public void ResetCounters() => Builds = 0;

    public void Initialize(GrassConfig config, GraphicsSettings graphics)
    {
        _config = config;
        _graphics = graphics;
        graphics.Changed += _ => _fadeDirty = true;
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

    /// <summary>
    /// A chunk's new grass (called with every uploaded mesh): unchanged hashes do nothing; otherwise the clumps are
    /// kept and the chunk is rebuilt by <see cref="Update"/> (in place when it is live).
    /// </summary>
    public void Set(Int3 coord, List<GrassClump> clumps, ulong shapeHash, ulong lightHash)
    {
        if (clumps.Count == 0)
        {
            Remove(coord);
            return;
        }
        if (_chunks.TryGetValue(coord, out var chunk))
        {
            if (chunk.Shape == shapeHash && chunk.Light == lightHash) return;
        }
        else _chunks[coord] = chunk = new ChunkGrass();
        chunk.Reshaped |= chunk.Live && chunk.Shape != shapeHash;
        if (chunk.Clumps.Length < clumps.Count) chunk.Clumps = new GrassClump[clumps.Count];
        clumps.CopyTo(chunk.Clumps);
        chunk.Count = clumps.Count;
        chunk.Flowers = 0;
        foreach (var c in clumps)
            if (c.Flower != 0) chunk.Flowers++;
        chunk.Shape = shapeHash;
        chunk.Light = lightHash;
        chunk.NeedsBuild = true;
    }

    public void Remove(Int3 coord)
    {
        if (!_chunks.Remove(coord, out var chunk)) return;
        Release(chunk);
    }

    /// <summary>Builds, thins and frees chunk grass around the camera; parts the grass around the player.</summary>
    public void Update(Vector3 camera, Vector3 playerFeet, double budgetMs)
    {
        RenderingServer.GlobalShaderParameterSet("strata_trample", new Vector4(playerFeet.X, playerFeet.Y, playerFeet.Z, TrampleRadius));
        var preset = _graphics.Preset;
        float distance = preset.GrassDistance;
        float density = preset.GrassDensity;
        if (_fadeDirty)
        {
            _material.SetShaderParameter("fade_start", distance * 0.72f);
            _material.SetShaderParameter("fade_end", distance);
            _flowerMaterial.SetShaderParameter("fade_start", distance * 0.5f);
            _flowerMaterial.SetShaderParameter("fade_end", distance * 0.7f);
            _fadeDirty = false;
        }
        int live = 0;
        long visible = 0;
        _builds.Clear();
        foreach (var (coord, chunk) in _chunks)
        {
            float d = DistanceToChunk(coord, camera);
            if (density <= 0f || d > distance + ReleaseMargin)
            {
                Release(chunk);
                continue;
            }
            if (!chunk.Live && d > distance) continue;
            if (!chunk.Live || chunk.NeedsBuild) _builds.Add((d, coord));
            if (!chunk.Live) continue;
            visible += ApplyLod(chunk, d, distance, density);
            live++;
        }
        PendingBuilds = _builds.Count;
        if (_builds.Count > 0)
        {
            _builds.Sort(BuildOrder.Instance);
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            int max = System.Math.Max(1, preset.GrassBuildsPerFrame), built = 0;
            foreach (var (d, coord) in _builds)
            {
                var chunk = _chunks[coord];
                // Reshaped live grass (an edit) is never held back: the old grass would float or stay buried.
                bool due = chunk.Live && chunk.Reshaped;
                if (!due && (built >= max || (built > 0 && System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds >= budgetMs))) continue;
                bool wasLive = chunk.Live;
                if (wasLive) visible -= chunk.Visible;
                Build(coord, chunk);
                Builds++;
                PendingBuilds--;
                built++;
                visible += ApplyLod(chunk, d, distance, density);
                if (!wasLive) live++;
            }
        }
        LiveChunks = live;
        VisibleClumps = visible;
    }

    /// <summary>Distance LOD: an even prefix of the clumps, the simpler clump mesh further out. Returns the drawn count.</summary>
    private int ApplyLod(ChunkGrass chunk, float d, float distance, float density)
    {
        float thin = 1f - 0.65f * Mathf.SmoothStep(distance * 0.3f, distance, d);
        int count = GrassScatter.VisibleCount(new System.ReadOnlySpan<float>(chunk.LiveRanks, 0, chunk.Allocated), density * thin);
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
        return count;
    }

    private static float DistanceToChunk(Int3 coord, Vector3 p)
    {
        var min = new Vector3(coord.X, coord.Y, coord.Z) * ChunkMeters;
        var max = min + Vector3.One * ChunkMeters;
        return p.Clamp(min, max).DistanceTo(p);
    }

    /// <summary>Nearest first, then X, Y, Z (deterministic order).</summary>
    private sealed class BuildOrder : IComparer<(float Distance, Int3 Coord)>
    {
        public static readonly BuildOrder Instance = new();

        public int Compare((float Distance, Int3 Coord) a, (float Distance, Int3 Coord) b)
        {
            int c = a.Distance.CompareTo(b.Distance);
            if (c != 0) return c;
            if (a.Coord.X != b.Coord.X) return a.Coord.X.CompareTo(b.Coord.X);
            if (a.Coord.Y != b.Coord.Y) return a.Coord.Y.CompareTo(b.Coord.Y);
            return a.Coord.Z.CompareTo(b.Coord.Z);
        }
    }

    public override void _ExitTree()
    {
        foreach (var chunk in _chunks.Values) Release(chunk);
        _chunks.Clear();
    }
}
