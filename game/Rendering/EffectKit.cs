using System.Collections.Generic;
using Godot;
using Strata.Core.Presentation;

namespace Strata.Game.Rendering;

/// <summary>The kinds of burst <see cref="EffectKit"/> can emit.</summary>
public enum EffectRecipe
{
    /// <summary>Solid tumbling chips (mining, gore splinters).</summary>
    Chips,

    /// <summary>Soft quads that drift down (hit dust, blood).</summary>
    Dust,

    /// <summary>Glowing streaks (metal hits, hot sparks).</summary>
    Sparks,

    /// <summary>Soft rising clouds (smoke, steam, death puffs).</summary>
    Puff,

    /// <summary>Droplets thrown up and falling (water and lava splashes, gel).</summary>
    Splash,

    /// <summary>Slow-falling flutter (leaves, petals).</summary>
    Leaves,
}

/// <summary>
/// Pooled one-shot particle bursts for every hit, mining and death effect: per recipe a pool of CPU emitters sharing a
/// voxel-lit material (shaders/particle_lit.gdshader), each burst lit by the voxel light where it happens and varied
/// between a lighter and darker shade of its colour. Counts scale by the graphics preset's <c>effectsScale</c>; at most
/// <see cref="BurstsPerFrame"/> bursts start per frame and about <see cref="MaxLive"/> particles live at once (the oldest
/// emitter is reused when the budget is spent). No allocation per burst once the pools are warm.
/// </summary>
public sealed partial class EffectKit : Node3D
{
    public const int BurstsPerFrame = 8;
    public const int MaxLive = 600;
    private const int PoolPerRecipe = 12;
    private static readonly StringName LightParam = "voxel_light";

    private readonly List<Emitter>[] _pools = new List<Emitter>[6];
    private readonly Mesh[] _meshes = new Mesh[6];
    private Strata.Core.Voxels.VoxelWorld? _world;
    private float _scale = 0.7f;
    private int _startedThisFrame;
    private ulong _frame;
    private int _live;

    /// <summary>The kit of the running world (set at launch), for effect nodes created later.</summary>
    public static EffectKit? Instance { get; private set; }

    /// <summary>Bursts started so far (probes).</summary>
    public int Bursts { get; private set; }

    public void Initialize(Strata.Core.Voxels.VoxelWorld world, GraphicsSettings graphics)
    {
        Name = "EffectKit";
        _world = world;
        Instance = this;
        _scale = graphics.Preset.EffectsScale;
        graphics.Changed += p => _scale = p.EffectsScale;
        for (int r = 0; r < _pools.Length; r++)
        {
            _pools[r] = new List<Emitter>(PoolPerRecipe);
            var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/particle_lit.gdshader") };
            material.SetShaderParameter("style", (EffectRecipe)r switch { EffectRecipe.Chips => 0, EffectRecipe.Sparks => 2, _ => 1 });
            Mesh mesh = (EffectRecipe)r == EffectRecipe.Chips
                ? new BoxMesh { Size = Vector3.One * 0.07f, Material = material }
                : new QuadMesh { Size = new Vector2(0.12f, 0.12f), Material = material };
            _meshes[r] = mesh;
            for (int i = 0; i < PoolPerRecipe; i++) _pools[r].Add(new Emitter(this, (EffectRecipe)r, mesh));
        }
        InitializeStatus(graphics);
        // Pre-warm: one hidden burst per recipe so the pipelines compile while the world loads.
        for (int r = 0; r < _pools.Length; r++) _pools[r][0].Start(new Vector3(0, -10000, 0), Colors.White, 1, 1f, null, 0f, Colors.White, this);
    }

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>
    /// Emits a burst of <paramref name="count"/> particles (scaled by the preset) at a world position, thrown at about
    /// <paramref name="speed"/> m/s (biased along <paramref name="dir"/> when given), coloured around
    /// <paramref name="color"/> ± <paramref name="variance"/>.
    /// </summary>
    public void Burst(EffectRecipe recipe, Vector3 position, Color color, int count, float speed, Vector3? dir = null, float variance = 0.15f)
    {
        ulong frame = Engine.GetProcessFrames();
        if (frame != _frame)
        {
            _frame = frame;
            _startedThisFrame = 0;
        }
        if (_startedThisFrame >= BurstsPerFrame || _world == null) return;
        int n = Mathf.Clamp(Mathf.RoundToInt(count * _scale), count > 0 ? 1 : 0, 64);
        if (n == 0) return;
        var emitter = Pick(_pools[(int)recipe], n);
        if (emitter == null) return;
        _startedThisFrame++;
        Bursts++;
        var light = LightProbe.Sample(_world, position);
        emitter.Start(position, color, n, speed, dir, variance, light, this);
    }

    /// <summary>A free emitter, else (or when the live budget is spent) the oldest one of this recipe.</summary>
    private Emitter? Pick(List<Emitter> pool, int n)
    {
        _live = 0;
        foreach (var list in _pools)
            foreach (var e in list) _live += e.LiveCount;
        Emitter? oldest = null;
        foreach (var e in pool)
        {
            if (!e.Busy && _live + n <= MaxLive) return e;
            if (oldest == null || e.StartedMs < oldest.StartedMs) oldest = e;
        }
        return oldest;
    }

    /// <summary>One pooled emitter and its colour ramp (reused, never reallocated).</summary>
    private sealed class Emitter
    {
        private readonly CpuParticles3D _node;
        private readonly Gradient _ramp = new();
        private readonly EffectRecipe _recipe;

        public Emitter(Node3D parent, EffectRecipe recipe, Mesh mesh)
        {
            _recipe = recipe;
            _ramp.SetColor(0, Colors.White);
            _ramp.SetColor(1, Colors.White);
            _node = new CpuParticles3D
            {
                Emitting = false,
                OneShot = true,
                Explosiveness = 0.92f,
                Mesh = mesh,
                ColorInitialRamp = _ramp,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                LocalCoords = false,
            };
            Configure(_node, recipe);
            parent.AddChild(_node);
        }

        public bool Busy => _node.Emitting;
        public ulong StartedMs { get; private set; }
        public int LiveCount => _node.Emitting ? _node.Amount : 0;

        public void Start(Vector3 at, Color color, int n, float speed, Vector3? dir, float variance, Color light, EffectKit kit)
        {
            StartedMs = Time.GetTicksMsec();
            _ramp.SetColor(0, color.Lightened(variance));
            _ramp.SetColor(1, color.Darkened(variance));
            _node.GlobalPosition = at;
            if (_node.Amount != n) _node.Amount = n;
            _node.Direction = dir is { } d && d.LengthSquared() > 1e-4f ? d.Normalized() : Vector3.Up;
            _node.Spread = dir == null ? (_recipe == EffectRecipe.Puff ? 180f : 70f) : 40f;
            _node.InitialVelocityMin = speed * 0.4f;
            _node.InitialVelocityMax = speed;
            _node.SetInstanceShaderParameter(LightParam, light);
            _node.Restart();
            _node.Emitting = true;
        }

        private static void Configure(CpuParticles3D p, EffectRecipe recipe)
        {
            switch (recipe)
            {
                case EffectRecipe.Chips:
                    p.Lifetime = 0.8;
                    p.Gravity = new Vector3(0, -14f, 0);
                    p.ScaleAmountMin = 0.5f;
                    p.ScaleAmountMax = 1.3f;
                    p.AngularVelocityMin = -360f;
                    p.AngularVelocityMax = 360f;
                    p.EmissionShape = CpuParticles3D.EmissionShapeEnum.Box;
                    p.EmissionBoxExtents = Vector3.One * 0.2f;
                    break;
                case EffectRecipe.Dust:
                    p.Lifetime = 0.6;
                    p.Gravity = new Vector3(0, -5f, 0);
                    p.DampingMin = p.DampingMax = 2f;
                    p.ScaleAmountMin = 0.6f;
                    p.ScaleAmountMax = 1.4f;
                    p.ScaleAmountCurve = FadeCurve();
                    break;
                case EffectRecipe.Sparks:
                    p.Lifetime = 0.35;
                    p.Gravity = new Vector3(0, -9f, 0);
                    p.ScaleAmountMin = 0.25f;
                    p.ScaleAmountMax = 0.5f;
                    p.ScaleAmountCurve = FadeCurve();
                    break;
                case EffectRecipe.Puff:
                    p.Lifetime = 0.9;
                    p.Gravity = new Vector3(0, 1.2f, 0);
                    p.DampingMin = p.DampingMax = 3f;
                    p.ScaleAmountMin = 1.5f;
                    p.ScaleAmountMax = 3f;
                    p.ScaleAmountCurve = FadeCurve();
                    break;
                case EffectRecipe.Splash:
                    p.Lifetime = 0.7;
                    p.Gravity = new Vector3(0, -12f, 0);
                    p.ScaleAmountMin = 0.4f;
                    p.ScaleAmountMax = 0.8f;
                    break;
                case EffectRecipe.Leaves:
                    p.Lifetime = 1.6;
                    p.Gravity = new Vector3(0, -1.5f, 0);
                    p.DampingMin = p.DampingMax = 1.5f;
                    p.ScaleAmountMin = 0.7f;
                    p.ScaleAmountMax = 1.1f;
                    p.AngularVelocityMin = -180f;
                    p.AngularVelocityMax = 180f;
                    break;
            }
        }

        private static Curve FadeCurve()
        {
            var c = new Curve();
            c.AddPoint(new Vector2(0f, 1f));
            c.AddPoint(new Vector2(0.7f, 0.8f));
            c.AddPoint(new Vector2(1f, 0f));
            return c;
        }
    }
}
