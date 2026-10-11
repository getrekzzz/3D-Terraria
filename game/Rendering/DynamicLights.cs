using System;
using System.Collections.Generic;
using Godot;
using Strata.Core.Common;
using Strata.Core.Presentation;
using Strata.Core.Session;

namespace Strata.Game.Rendering;

/// <summary>
/// Real (shadowless) lights for the light sources nearest the camera: placed torches and stations, lava hotspots and
/// glowing creatures. Baked voxel light gives every source its soft glow; these add a longer reach and directional
/// shading to the few that matter most: a placed torch's light carries to about 13 m (data/config/lighting.json
/// <c>torch</c>), well past the 7.5 m edge of its baked light. The pool size comes from the graphics preset
/// (<c>dynamicLights</c>, 4 even on low), so a swarm of fireflies can never turn into dozens of lights.
/// </summary>
public sealed partial class DynamicLights : Node3D
{
    /// <summary>Sources further than this from the camera are ignored (lights fade out before it).</summary>
    public const float Radius = 34f;

    public enum Kind { Steady, Fire, Blink, Lava }

    /// <summary>
    /// A candidate light: colour normalised to its brightest channel, <paramref name="Peak"/> its 0..1 level;
    /// <paramref name="Seed"/> desynchronises flicker (a stable value per source); <paramref name="Track"/> follows a
    /// moving source every frame; <paramref name="Attenuation"/> is Godot's omni falloff (lower carries further).
    /// </summary>
    public readonly record struct Source(Vector3 Position, Color Color, float Peak, Kind Kind, float Range, float Energy, float Seed,
        Func<Vector3>? Track = null, float Attenuation = 1.1f);

    private readonly List<OmniLight3D> _pool = new();
    private readonly List<Func<Vector3, float, IEnumerable<Source>>> _providers = new();
    private readonly List<Source> _assigned = new();
    // Reused by the 4 Hz refresh: ranking the candidates allocates nothing.
    private readonly List<Candidate> _candidates = new();
    private TorchSettings _torch = null!;
    private double _refresh;
    private float _time;

    /// <summary>A source and its rank (lower is chosen first).</summary>
    private readonly record struct Candidate(float Score, Source Source);

    private static readonly Comparison<Candidate> ByScore = (a, b) => a.Score.CompareTo(b.Score);

    public void Initialize(GraphicsSettings graphics)
    {
        Name = "DynamicLights";
        _torch = graphics.Lighting.Torch;
        Resize(graphics.Preset.DynamicLights);
        graphics.Changed += p => Resize(p.DynamicLights);
    }

    /// <summary>Adds a source of candidates: (camera position, radius) → sources.</summary>
    public void AddProvider(Func<Vector3, float, IEnumerable<Source>> provider) => _providers.Add(provider);

    /// <summary>
    /// Placed objects that give light (torches, candles, furnaces), switched-off ones excluded. Energy and range scale
    /// with the light's peak channel (lighting.json <c>torch.placed*</c>): a full torch reaches about 13 m.
    /// </summary>
    public IEnumerable<Source> PlacedLights(GameWorld game, World.ObjectPlacement placement, Vector3 eye, float radius)
    {
        int r = (int)(radius / Units.CellSize);
        var c = new Int3((int)MathF.Floor(eye.X / Units.CellSize), (int)MathF.Floor(eye.Y / Units.CellSize), (int)MathF.Floor(eye.Z / Units.CellSize));
        foreach (var o in game.Session.World.TileObjects.InBox(new Aabb3i(c - Int3.One * r, c + Int3.One * r)))
        {
            if (o.Def.Light is not { Length: 3 } l || (o.Active && !o.IsDoor)) continue;
            int max = Math.Max(l[0], Math.Max(l[1], l[2]));
            if (max < 6) continue; // glowing mushrooms and the like: the baked glow is enough
            Vector3 at;
            if (o.Def.Flame is { } flame) at = Flames.BaseOf(flame, placement.TransformOf(o)) + Vector3.Up * 0.12f;
            else
            {
                var fp = o.Footprint;
                at = new Vector3(o.Anchor.X + fp.X * 0.5f, o.Anchor.Y + fp.Y - 0.5f, o.Anchor.Z + fp.Z * 0.5f) * Units.CellSize;
            }
            float peak = max / 15f;
            yield return new Source(at, new Color(l[0] / (float)max, l[1] / (float)max, l[2] / (float)max), peak,
                o.Def.Flame != null ? Kind.Fire : Kind.Steady, _torch.PlacedRangeBase + peak * _torch.PlacedRangePerPeak,
                _torch.PlacedEnergy * peak, Seed(at), Attenuation: _torch.PlacedAttenuation);
        }
    }

    public override void _Process(double delta)
    {
        using var profile = Strata.Core.Diagnostics.Metrics.Frame.Time("gd.dynamic_lights");
        _time += (float)delta;
        if (_pool.Count == 0) return;
        var camera = GetViewport()?.GetCamera3D();
        if (camera == null) return;
        _refresh -= delta;
        if (_refresh <= 0)
        {
            _refresh = 0.25;
            Assign(camera.GlobalPosition);
        }
        for (int i = 0; i < _assigned.Count; i++)
        {
            var s = _assigned[i];
            _pool[i].LightEnergy = s.Energy * Flicker(s.Kind, s.Seed);
            if (s.Track != null) _pool[i].GlobalPosition = s.Track();
        }
    }

    private void Assign(Vector3 eye)
    {
        _candidates.Clear();
        // Nearest first, a dim source counting as further away than a bright one at the same distance.
        foreach (var provider in _providers)
            foreach (var s in provider(eye, Radius))
                _candidates.Add(new Candidate(s.Position.DistanceTo(eye) / (0.4f + s.Peak), s));
        _candidates.Sort(ByScore);
        _assigned.Clear();
        for (int i = 0; i < _pool.Count; i++)
        {
            var light = _pool[i];
            if (i >= _candidates.Count)
            {
                light.Visible = false;
                continue;
            }
            var s = _candidates[i].Source;
            _assigned.Add(s);
            light.Visible = true;
            light.GlobalPosition = s.Position;
            light.LightColor = s.Color;
            light.OmniRange = s.Range;
            light.OmniAttenuation = s.Attenuation;
        }
    }

    private void Resize(int count)
    {
        while (_pool.Count > count)
        {
            _pool[^1].QueueFree();
            _pool.RemoveAt(_pool.Count - 1);
        }
        while (_pool.Count < count)
        {
            var light = new OmniLight3D
            {
                ShadowEnabled = false,
                Visible = false,
                LightSpecular = 0.4f,
                LightVolumetricFogEnergy = 0.6f,
                DistanceFadeEnabled = true,
                DistanceFadeBegin = Radius - 10f,
                DistanceFadeLength = 8f,
            };
            AddChild(light);
            _pool.Add(light);
        }
        if (_assigned.Count > _pool.Count) _assigned.RemoveRange(_pool.Count, _assigned.Count - _pool.Count);
        _refresh = 0;
    }

    private float Flicker(Kind kind, float phase)
    {
        float t = _time;
        float p = phase * Mathf.Tau;
        return kind switch
        {
            Kind.Fire => 1f + Mathf.Sin(t * 17.3f + p) * 0.06f + Mathf.Sin(t * 7.1f + 2f * p) * 0.05f + Mathf.Sin(t * 23.9f + 3f * p) * 0.035f,
            Kind.Lava => 1f + Mathf.Sin(t * 1.1f + p) * 0.12f + Mathf.Sin(t * 2.9f + 2f * p) * 0.06f,
            Kind.Blink => Mathf.SmoothStep(-0.2f, 0.7f, Mathf.Sin(t * 1.4f + p)), // fireflies glow and go dark
            _ => 1f,
        };
    }

    /// <summary>A stable 0..1 value for a fixed position.</summary>
    public static float Seed(Vector3 p) => Mathf.PosMod(Mathf.Sin(p.X * 12.9898f + p.Y * 78.233f + p.Z * 37.719f) * 43758.547f, 1f);
}
