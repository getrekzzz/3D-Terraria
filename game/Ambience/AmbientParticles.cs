using System.Collections.Generic;
using Godot;
using Strata.Core.Presentation;
using Strata.Game.World;

namespace Strata.Game.Ambience;

/// <summary>
/// Pooled GPU emitters for the ambient particles around the camera (dust motes, mushroom spores, underworld embers,
/// falling leaves, fireflies), one per kind, created once and faded in or out as the ambience rules for the place
/// change; their counts are the rules' counts scaled by the graphics preset (data/config/ambience.json). Dust and
/// leaves are tinted by the light at the camera, so motes show in torchlight and stay hidden in the dark. Cave drips
/// are positioned on real ceilings by <see cref="AmbientDrips"/>.
/// </summary>
public sealed partial class AmbientParticles : Node3D
{
    private static readonly string[] GpuKinds = { "dust", "spores", "embers", "leaves", "fireflies" };

    private WorldRuntime _runtime = null!;
    private AmbienceConfig _config = null!;
    private Rendering.GraphicsSettings _graphics = null!;
    private readonly Dictionary<string, Emitter> _emitters = new();
    private readonly Dictionary<string, int> _wanted = new();
    private AmbientDrips _drips = null!;
    private SkyHeights? _ownSky;
    private float _lightSample;
    private readonly Vector4[] _canopies = new Vector4[8];
    private readonly float[] _canopyDistance = new float[8];
    private int _canopyCount;

    /// <summary>Particles alive at most now over every kind (probe <c>ambience.particles</c>).</summary>
    public int Alive
    {
        get
        {
            int n = _drips.Active;
            foreach (var e in _emitters.Values) n += e.Alive;
            return n;
        }
    }

    public void Initialize(WorldRuntime runtime, Rendering.GraphicsSettings graphics, AmbienceConfig config)
    {
        Name = "AmbientParticles";
        _runtime = runtime;
        _config = config;
        _graphics = graphics;
        for (int k = 0; k < GpuKinds.Length; k++) _emitters[GpuKinds[k]] = new Emitter(this, k);
        _drips = new AmbientDrips();
        AddChild(_drips);
        _drips.Initialize(runtime);
    }

    /// <summary>The kinds that belong where the camera is now (from the ambience rules).</summary>
    public void Select(List<AmbienceParticleRule> rules, Vector3 eye)
    {
        float scale = _config.ScaleFor(_graphics.Name);
        foreach (var k in GpuKinds) _wanted[k] = 0;
        int drips = 0;
        foreach (var r in rules)
        {
            int n = Mathf.RoundToInt(r.Count * scale);
            if (r.Kind == "drips") drips = Mathf.Max(drips, n);
            else _wanted[r.Kind] = Mathf.Max(_wanted[r.Kind], n);
        }
        _drips.Wanted = drips;
        if (_wanted["leaves"] > 0)
        {
            FindCanopies(eye);
            // A lone tree sheds a few leaves; a forest many.
            _wanted["leaves"] = Mathf.RoundToInt(_wanted["leaves"] * Mathf.Clamp(_canopyCount / 4f, 0f, 1f));
            _emitters["leaves"].SetCanopies(_canopies, _canopyCount);
        }
    }

    /// <summary>The (up to eight) nearest tall trees' canopies around the camera, nearest first.</summary>
    private void FindCanopies(Vector3 eye)
    {
        _canopyCount = 0;
        var cell = new Strata.Core.Common.Int3(Mathf.FloorToInt(eye.X / Strata.Core.Common.Units.CellSize),
            Mathf.FloorToInt(eye.Y / Strata.Core.Common.Units.CellSize), Mathf.FloorToInt(eye.Z / Strata.Core.Common.Units.CellSize));
        var box = new Strata.Core.Common.Aabb3i(cell - new Strata.Core.Common.Int3(32, 40, 32), cell + new Strata.Core.Common.Int3(32, 16, 32));
        foreach (var o in _runtime.Session.World.TileObjects.InBox(box))
        {
            if (o.Def.BreakTool != Strata.Core.Content.Defs.BreakTool.Axe || o.Def.Size[1] < 12) continue;
            var f = o.Footprint;
            var at = new Vector3(o.Anchor.X + f.X * 0.5f, o.Anchor.Y + f.Y * 0.78f, o.Anchor.Z + f.Z * 0.5f) * Strata.Core.Common.Units.CellSize;
            float d = at.DistanceSquaredTo(eye);
            int slot = _canopyCount < _canopies.Length ? _canopyCount++ : -1;
            if (slot < 0)
            {
                slot = 0;
                for (int i = 1; i < _canopies.Length; i++) if (_canopyDistance[i] > _canopyDistance[slot]) slot = i;
                if (_canopyDistance[slot] <= d) continue;
            }
            _canopies[slot] = new Vector4(at.X, at.Y, at.Z, f.Y * Strata.Core.Common.Units.CellSize * 0.22f);
            _canopyDistance[slot] = d;
        }
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        var camera = GetViewport()?.GetCamera3D();
        if (camera == null) return;
        var eye = camera.GlobalPosition;
        Position = eye;
        var sky = _runtime.Weather?.Heights ?? (_ownSky ??= new SkyHeights(_runtime.Session));
        if (_runtime.Weather == null) sky.Update(eye);
        _lightSample -= dt;
        Color light = default;
        bool relight = _lightSample <= 0f;
        if (relight)
        {
            _lightSample = 0.2f;
            var l = Rendering.LightProbe.SampleSmooth(_runtime.Session.World, eye);
            float skyLight = l.A * l.A * Mathf.Lerp(0.15f, 1f, _runtime.Environment.Daylight);
            light = new Color(Mathf.Max(l.R, skyLight), Mathf.Max(l.G, skyLight), Mathf.Max(l.B, skyLight * 1.05f));
        }
        foreach (var (kind, e) in _emitters) e.Drive(_wanted[kind], sky, relight ? light : null, dt);
    }

    /// <summary>One kind's GPU emitter: its amount grows to the largest count asked of it, and only a share lives.</summary>
    private sealed class Emitter
    {
        private static readonly StringName LightParam = "local_light", HeightsParam = "sky_heights", RectParam = "sky_rect",
            CanopiesParam = "canopies", CanopyCountParam = "canopy_count";
        private readonly GpuParticles3D _node;
        private readonly ShaderMaterial _processMaterial, _drawMaterial;
        private float _ratio;
        private int _target;
        private int _skyVersion = -1;

        public Emitter(Node3D parent, int kind)
        {
            _processMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/ambient_particles.gdshader") };
            _processMaterial.SetShaderParameter("kind", kind);
            _drawMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/ambient_mote.gdshader") };
            _drawMaterial.SetShaderParameter("kind", kind);
            var (tint, size, energy, lifetime) = kind switch
            {
                0 => (new Color(0.9f, 0.84f, 0.72f, 0.55f), 0.022f, 1f, 12f),
                1 => (new Color(0.3f, 0.65f, 1f, 0.9f), 0.045f, 2.6f, 10f),
                2 => (new Color(1f, 0.42f, 0.1f, 1f), 0.035f, 4f, 4f),
                3 => (new Color(0.34f, 0.48f, 0.16f, 1f), 0.08f, 1f, 9f),
                _ => (new Color(0.72f, 1f, 0.3f, 1f), 0.045f, 3.2f, 14f),
            };
            _drawMaterial.SetShaderParameter("tint", tint);
            _drawMaterial.SetShaderParameter("size", size);
            _drawMaterial.SetShaderParameter("energy", energy);
            _node = new GpuParticles3D
            {
                Name = GpuKinds[kind],
                ProcessMaterial = _processMaterial,
                DrawPass1 = new QuadMesh { Size = new Vector2(2f, 2f), Material = _drawMaterial },
                Lifetime = lifetime,
                Randomness = 0.5f,
                LocalCoords = false,
                Emitting = false,
                Amount = 1,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                VisibilityAabb = new Aabb(new Vector3(-20f, -20f, -20f), new Vector3(40f, 40f, 40f)),
            };
            parent.AddChild(_node);
        }

        public int Alive => _node.Emitting ? Mathf.RoundToInt(_node.Amount * _ratio) : 0;

        public void SetCanopies(Vector4[] canopies, int count)
        {
            _processMaterial.SetShaderParameter(CanopiesParam, canopies);
            _processMaterial.SetShaderParameter(CanopyCountParam, count);
        }

        public void Drive(int wanted, SkyHeights sky, Color? light, float dt)
        {
            // The pool only grows (changing the amount restarts the emitter); a smaller ask lowers the live share.
            if (wanted > _node.Amount) _node.Amount = wanted;
            if (wanted > 0) _target = wanted;
            float ratio = wanted > 0 ? (float)_target / _node.Amount : 0f;
            _ratio = Mathf.MoveToward(_ratio, ratio, dt * 0.35f);
            bool on = _ratio > 0.01f;
            if (_node.Emitting != on) _node.Emitting = on;
            if (!on) return;
            _node.AmountRatio = _ratio;
            if (light is { } l) _drawMaterial.SetShaderParameter(LightParam, new Vector3(l.R, l.G, l.B));
            if (sky.Version == _skyVersion) return;
            _skyVersion = sky.Version;
            _processMaterial.SetShaderParameter(HeightsParam, sky.Texture);
            _processMaterial.SetShaderParameter(RectParam, sky.Rect);
        }
    }
}
