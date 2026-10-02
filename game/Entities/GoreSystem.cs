using Godot;
using Strata.Core.Common;
using Strata.Core.Voxels;

namespace Strata.Game.Entities;

/// <summary>
/// Gore chunks thrown out when a creature dies: small boxes or balls that tumble, bounce off the voxels (World.IsSolid
/// at the next cell, bounce 0.35 with ground friction) and fade after 2.5 s. Drawn as two MultiMeshes (boxes, balls)
/// whose instance custom data carries the voxel light, sampled every 4th frame per chunk. The capacity follows the
/// preset's effects scale (16–96); when it is full the oldest chunk is reused. No allocation per frame.
/// </summary>
public sealed partial class GoreSystem : Node3D
{
    private const float Life = 2.5f, FadeTime = 0.4f, Gravity = 18f, Bounce = 0.35f;

    private struct Chunk
    {
        public bool Live, Ball;
        public Vector3 Position, Velocity, Spin, Angle, Size;
        public Color Color, Light;
        public float Age;
    }

    private VoxelWorld _world = null!;
    private Chunk[] _chunks = System.Array.Empty<Chunk>();
    private MultiMesh _boxes = null!, _balls = null!;
    private int _next;
    private int _frame;

    public int Live { get; private set; }

    /// <summary>The gore of the running world (probes).</summary>
    public static GoreSystem? Instance { get; private set; }

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
    }

    public void Initialize(VoxelWorld world, Rendering.GraphicsSettings graphics)
    {
        Name = "Gore";
        _world = world;
        Instance = this;
        var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/gore.gdshader") };
        _boxes = MakeMesh(new BoxMesh { Size = Vector3.One, Material = material });
        _balls = MakeMesh(new SphereMesh { Radius = 0.5f, Height = 1f, RadialSegments = 8, Rings = 4, Material = material });
        Resize(graphics.Preset.EffectsScale);
        graphics.Changed += p => Resize(p.EffectsScale);
    }

    private MultiMesh MakeMesh(Mesh mesh)
    {
        var mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            UseCustomData = true,
            Mesh = mesh,
        };
        AddChild(new MultiMeshInstance3D { Multimesh = mm, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        return mm;
    }

    private void Resize(float effectsScale)
    {
        int capacity = Mathf.Clamp(Mathf.RoundToInt(80f * effectsScale), 16, 96);
        if (capacity == _chunks.Length) return;
        _chunks = new Chunk[capacity];
        _next = 0;
        _boxes.InstanceCount = capacity;
        _balls.InstanceCount = capacity;
        _boxes.VisibleInstanceCount = 0;
        _balls.VisibleInstanceCount = 0;
    }

    /// <summary>Throws <paramref name="pieces"/> chunks of about <paramref name="size"/> m from a creature's centre.</summary>
    public void Spawn(Vector3 center, Vector3 halfExtents, int pieces, float size, Color color, bool balls, RandomNumberGenerator rng)
    {
        for (int i = 0; i < pieces && _chunks.Length > 0; i++)
        {
            ref var c = ref _chunks[_next];
            _next = (_next + 1) % _chunks.Length;
            c.Live = true;
            c.Ball = balls;
            c.Age = 0f;
            c.Position = center + new Vector3(rng.RandfRange(-1, 1) * halfExtents.X, rng.RandfRange(-0.5f, 1f) * halfExtents.Y, rng.RandfRange(-1, 1) * halfExtents.Z);
            c.Velocity = new Vector3(rng.RandfRange(-3f, 3f), rng.RandfRange(2.5f, 6f), rng.RandfRange(-3f, 3f));
            c.Spin = new Vector3(rng.RandfRange(-9, 9), rng.RandfRange(-9, 9), rng.RandfRange(-9, 9));
            c.Angle = Vector3.Zero;
            float s = size * rng.RandfRange(0.7f, 1.3f);
            c.Size = new Vector3(s, s * rng.RandfRange(0.6f, 1f), s * rng.RandfRange(0.7f, 1.2f));
            c.Color = color.Darkened(rng.RandfRange(0f, 0.25f));
            c.Light = Rendering.LightProbe.Sample(_world, c.Position);
        }
    }

    public override void _Process(double delta)
    {
        using var profile = Strata.Core.Diagnostics.Metrics.Frame.Time("gd.gore");
        float dt = Mathf.Min((float)delta, 0.05f);
        _frame++;
        int boxes = 0, balls = 0, live = 0;
        for (int i = 0; i < _chunks.Length; i++)
        {
            ref var c = ref _chunks[i];
            if (!c.Live) continue;
            c.Age += dt;
            if (c.Age >= Life)
            {
                c.Live = false;
                continue;
            }
            Step(ref c, dt);
            if ((_frame + i) % 4 == 0) c.Light = Rendering.LightProbe.Sample(_world, c.Position);
            float alpha = Mathf.Clamp((Life - c.Age) / FadeTime, 0f, 1f);
            var basis = Basis.FromEuler(c.Angle).Scaled(c.Size);
            var mm = c.Ball ? _balls : _boxes;
            int slot = c.Ball ? balls++ : boxes++;
            mm.SetInstanceTransform(slot, new Transform3D(basis, c.Position));
            mm.SetInstanceColor(slot, new Color(c.Color.R, c.Color.G, c.Color.B, alpha));
            mm.SetInstanceCustomData(slot, c.Light);
            live++;
        }
        _boxes.VisibleInstanceCount = boxes;
        _balls.VisibleInstanceCount = balls;
        Live = live;
    }

    private void Step(ref Chunk c, float dt)
    {
        c.Velocity.Y -= Gravity * dt;
        var next = c.Position + c.Velocity * dt;
        // Bounce off whichever axis would enter a solid cell.
        if (Solid(new Vector3(c.Position.X, next.Y - c.Size.Y * 0.5f, c.Position.Z)))
        {
            if (c.Velocity.Y < 0f)
            {
                c.Velocity.Y = -c.Velocity.Y * Bounce;
                c.Velocity.X *= 0.6f; // ground friction
                c.Velocity.Z *= 0.6f;
                c.Spin *= 0.5f;
                if (c.Velocity.Y < 0.6f) c.Velocity.Y = 0f;
            }
            else c.Velocity.Y = 0f;
            next.Y = c.Position.Y;
        }
        if (Solid(new Vector3(next.X, c.Position.Y, c.Position.Z)))
        {
            c.Velocity.X = -c.Velocity.X * Bounce;
            next.X = c.Position.X;
        }
        if (Solid(new Vector3(c.Position.X, c.Position.Y, next.Z)))
        {
            c.Velocity.Z = -c.Velocity.Z * Bounce;
            next.Z = c.Position.Z;
        }
        c.Position = next;
        c.Angle += c.Spin * dt;
    }

    private bool Solid(Vector3 p) =>
        _world.IsSolid(Mathf.FloorToInt(p.X / Units.CellSize), Mathf.FloorToInt(p.Y / Units.CellSize), Mathf.FloorToInt(p.Z / Units.CellSize));
}
