using System.Collections.Generic;
using Godot;
using Strata.Core.Common;
using Strata.Core.Presentation;
using Strata.Game.Audio;
using Strata.Game.World;

namespace Strata.Game.Ambience;

/// <summary>
/// The living background of the world (data/config/ambience.json): sound beds chosen by the biome, layer and time
/// around the camera (birdsong by day, crickets at night, surf, cave air and drips, the underworld's lava), cross-faded
/// on the Ambient bus; cave acoustics (<see cref="AudioEnvironment"/>); and the ambient particles near the camera
/// (<see cref="AmbientParticles"/>). The place is re-read a few times a second; loops and particles fade, never pop.
/// </summary>
public sealed partial class AmbienceDirector : Node3D
{
    private const float SampleSeconds = 0.5f;

    private WorldRuntime _runtime = null!;
    private AmbienceConfig _config = null!;
    private AmbientParticles _particles = null!;
    private readonly Dictionary<string, (AmbienceLoops.Fader Fader, bool Outdoors)> _loops = new();
    private readonly Dictionary<string, float> _targets = new();
    private readonly List<AmbienceLoopRule> _loopRules = new();
    private readonly List<AmbienceParticleRule> _particleRules = new();
    private readonly Strata.Core.Conditions.ConditionContext _ctx = new();
    private float _sample;

    /// <summary>The loudest bed now (probe <c>ambience.loop</c> reports how many are audible).</summary>
    public int AudibleLoops { get; private set; }

    public AmbientParticles Particles => _particles;

    public void Initialize(WorldRuntime runtime, Rendering.GraphicsSettings graphics, AmbienceConfig config)
    {
        Name = "Ambience";
        _runtime = runtime;
        _config = config;
        AudioEnvironment.Ensure();
        foreach (var rule in config.Loops)
        {
            if (_loops.ContainsKey(rule.Loop)) continue;
            _loops[rule.Loop] = (AmbienceLoops.Player(this, rule.Loop, rule.Outdoors ? AudioEnvironment.Outdoors : AudioBuses.Ambient), rule.Outdoors);
            _targets[rule.Loop] = 0f;
        }
        _particles = new AmbientParticles();
        AddChild(_particles);
        _particles.Initialize(runtime, graphics, config);
    }

    public override void _Process(double delta)
    {
        using var profile = Strata.Core.Diagnostics.Metrics.Frame.Time("gd.ambience");
        float dt = (float)delta;
        var camera = GetViewport()?.GetCamera3D();
        if (camera == null) return;
        var env = _runtime.Environment;
        _sample -= dt;
        if (_sample <= 0f)
        {
            _sample = SampleSeconds;
            Sample(camera.GlobalPosition);
        }
        AudioEnvironment.Update(env.Outdoor, env.Depth, _config.Reverb, dt);
        int audible = 0;
        foreach (var (id, loop) in _loops)
        {
            float target = _targets[id];
            // Surface beds thin out under a roof (the low-pass does the rest); cave beds fill enclosed spaces.
            if (loop.Outdoors) target *= Mathf.Lerp(0.45f, 1f, env.Outdoor);
            loop.Fader.Drive(target, dt, 0.25f);
            if (loop.Fader.Level > 0.01f) audible++;
        }
        AudibleLoops = audible;
    }

    private void Sample(Vector3 eye)
    {
        var session = _runtime.Session;
        var cell = new Int3(Mathf.FloorToInt(eye.X / Units.CellSize), Mathf.FloorToInt(eye.Y / Units.CellSize), Mathf.FloorToInt(eye.Z / Units.CellSize));
        var ctx = session.ContextAt(cell, _ctx);
        bool day = session.Clock.IsDay;
        AmbienceConfig.Select(_config.Loops, ctx.Biomes, ctx.Layer, day, _loopRules);
        foreach (var id in _loops.Keys) _targets[id] = 0f;
        foreach (var r in _loopRules) _targets[r.Loop] = Mathf.Max(_targets[r.Loop], r.Volume);
        AmbienceConfig.Select(_config.Particles, ctx.Biomes, ctx.Layer, day, _particleRules);
        _particles.Select(_particleRules, eye);
    }
}
