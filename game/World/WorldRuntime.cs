using Godot;
using Strata.Core.Diagnostics;
using Strata.Core.Common;
using Strata.Core.Session;
using Strata.Core.Streaming;
using Strata.Game.Ambience;
using Strata.Game.Bootstrap;

namespace Strata.Game.World;

/// <summary>
/// Hosts a live <see cref="GameWorld"/> in the scene: drives streaming and the fixed tick, uploads meshes and
/// colliders, and runs the environment. Gameplay presentation nodes (player, entities, UI) attach under it.
/// </summary>
public sealed partial class WorldRuntime : Node3D
{
    private GameServices _services = null!;
    private ChunkRenderer _renderer = null!;
    private GrassRenderer? _grass;
    private HorizonRenderer _horizon = null!;
    private float _streamDistance;
    private double _cpuFrameMs;
    private ColliderManager _colliders = null!;
    private EnvironmentController _environment = null!;

    public GameWorld Game { get; private set; } = null!;
    public WorldSession Session => Game.Session;
    public ChunkStreamer Streamer => Game.Streamer;
    public JobSystem Jobs => Game.Jobs;
    public ChunkRenderer Renderer => _renderer;
    public GrassRenderer? Grass => _grass;
    public ColliderManager Colliders => _colliders;
    public EnvironmentController Environment => _environment;
    public GameServices Services => _services;

    /// <summary>Rain, snow and sand around the camera (added with the world effects).</summary>
    public WeatherRenderer? Weather { get; set; }

    /// <summary>Sound beds, cave acoustics and ambient particles (added with the world effects).</summary>
    public AmbienceDirector? Ambience { get; set; }

    /// <summary>Node whose position drives streaming (player or camera).</summary>
    public Node3D? Focus { get; set; }

    /// <summary>This frame's main-thread budgets (computed first in <see cref="_Process"/>; presentation nodes read it).</summary>
    public FrameBudgets Budgets { get; private set; } = FrameBudgetPlanner.Loading;

    /// <summary>
    /// A mesh result was applied to a chunk; says whether its collision changed (<see cref="ColliderChange.Pending"/>:
    /// new bodies follow with <see cref="ColliderManager.BodyBuilt"/>) or not (a light- or liquid-only remesh).
    /// </summary>
    public event System.Action<Int3, ColliderChange>? MeshApplied;

    /// <summary>When false the fixed tick is paused (menus, loading).</summary>
    public bool Simulating { get; set; } = true;

    public void Initialize(GameServices services, GameWorld game)
    {
        _services = services;
        Game = game;

        _renderer = new ChunkRenderer { Name = "Chunks" };
        var terrain = new TerrainMaterials(services.Render);
        terrain.ApplyGraphics(services.Graphics.Preset);
        services.Graphics.Changed += terrain.ApplyGraphics;
        _renderer.Initialize(terrain);
        AddChild(_renderer);
        if (services.Render.Grass is { } grass)
        {
            _grass = new GrassRenderer { Name = "Grass" };
            _grass.Initialize(grass, services.Graphics);
            AddChild(_grass);
        }
        _colliders = new ColliderManager { Name = "Colliders" };
        AddChild(_colliders);
        _environment = new EnvironmentController { Name = "Environment" };
        _environment.Initialize(services.Graphics, game.Session.Settings.Seed);
        if (game.Session.Settings.Has(Strata.Core.World.SecretSeed.TheConstant)) _environment.Dim = 0.6f; // a dim world (X-07)
        _renderer.SetSealedShadows(services.Graphics.Preset.TorchShadows);
        services.Graphics.Changed += p => _renderer.SetSealedShadows(p.TorchShadows);
        _streamDistance = game.Streamer.Settings.MeshRadius * ChunkMath.Size * Units.CellSize;
        _environment.ViewDistance = _streamDistance;
        AddChild(_environment);
        _horizon = new HorizonRenderer { Name = "Horizon" };
        _horizon.Initialize(new Strata.Core.Presentation.HorizonField(game.Session.Generator), services.Graphics);
        AddChild(_horizon);

        Streamer.MeshReady += result =>
        {
            _renderer.Enqueue(result);
            _grass?.Set(result.Coord, result.Grass, result.GrassShapeHash, result.GrassLightHash);
            var change = _colliders.SetData(result.Coord, result.Collision, result.Version);
            MeshApplied?.Invoke(result.Coord, change);
        };
        // Player edits rebuild their colliders before distance-ordered work.
        System.Func<Int3, int> chunkVersion = c => Session.World.GetChunk(c)?.Version ?? 0;
        game.Editor.ChunksDirty += dirty => _colliders.Prioritize(dirty, chunkVersion);
        Streamer.ChunkRemoved += coord =>
        {
            _renderer.Remove(coord);
            _grass?.Remove(coord);
            _colliders.Remove(coord);
        };
        game.Autosaved += chunks => services.Log.Info("world", $"Autosaved ({chunks} chunks).");
    }

    public Int3 FocusCell
    {
        get
        {
            var p = Focus?.GlobalPosition ?? Vector3.Zero;
            return new Int3(Mathf.FloorToInt(p.X / Units.CellSize), Mathf.FloorToInt(p.Y / Units.CellSize), Mathf.FloorToInt(p.Z / Units.CellSize));
        }
    }

    /// <summary>True once the chunks around a cell are meshed and have colliders (safe to drop a body there).</summary>
    public bool IsAreaReady(Int3 cell)
    {
        var chunk = ChunkMath.CellToChunk(cell);
        for (int dy = -1; dy <= 0; dy++)
        {
            var c = chunk + new Int3(0, dy, 0);
            if (!Session.World.ChunkInBounds(c)) continue;
            if (!Streamer.IsMeshed(c) || !_colliders.HasBody(c)) return false;
        }
        return true;
    }

    public override void _Process(double delta)
    {
        var prof = Strata.Core.Diagnostics.Metrics.Frame;
        using var frame = prof.Time("gd.world_runtime");
        prof.Record("frame.delta", delta * 1000.0);
        var focus = FocusCell;
        // Big budgets only while loading (frozen, or the ground around the focus not meshed yet after a spawn or
        // teleport); during play they stay small and shrink after frames that were slow on the CPU.
        bool loading = Focus is Player.PlayerController { Frozen: true } || !Streamer.IsSettledNear(focus);
        double cpuMs = (Performance.GetMonitor(Performance.Monitor.TimeProcess) + Performance.GetMonitor(Performance.Monitor.TimePhysicsProcess)) * 1000.0;
        _cpuFrameMs = _cpuFrameMs * 0.75 + cpuMs * 0.25;
        var budgets = Budgets = FrameBudgetPlanner.Plan(loading, Jobs.Completions.Count, _cpuFrameMs);
        using (prof.Time("gd.stream_and_jobs")) Game.Update(focus, budgets.JobsMs);
        using (prof.Time("gd.mesh_uploads")) _renderer.ProcessUploads(budgets.UploadsMs);
        using (prof.Time("gd.colliders")) _colliders.UpdateFocus(ChunkMath.CellToChunk(focus), budgets.CollidersMs);
        _environment.EventTint = Game.Events.SkyTint is { } tint ? Color.FromHtml(tint) : null;
        if (Engine.GetProcessFrames() % 15 == 0) _environment.Biome = BiomeAround(focus);
        var camera = GetViewport().GetCamera3D();
        var eye = camera?.GlobalPosition ?? Focus?.GlobalPosition ?? Vector3.Zero;
        var st = Streamer.Settings;
        var eyeCell = new Int3(Mathf.FloorToInt(eye.X / Units.CellSize), Mathf.FloorToInt(eye.Y / Units.CellSize), Mathf.FloorToInt(eye.Z / Units.CellSize));
        using (prof.Time("gd.culling"))
            _renderer.UpdateCulling(ChunkMath.CellToChunk(eyeCell), st.MeshRadius + 1, System.Math.Max(st.MeshUp, st.MeshDown) + 1, CaveCulling);
        using (prof.Time("gd.grass")) _grass?.Update(eye, Focus?.GlobalPosition ?? eye, budgets.GrassMs);
        UpdateDistance(camera, eye, focus);
        var terrain = Session.Generator.Terrain;
        float surface = terrain.SurfaceHeight(eye.X / Units.CellSize, eye.Z / Units.CellSize);
        float skyExposure = Rendering.LightProbe.SampleSmooth(Session.World, eye).A;
        float depth = Session.Generator.Plan.Bands.Inverted // Don't Dig Up: inside the rock above the surface's open air
            ? Strata.Core.WorldGen.Fields.TerrainField.SurfaceDepth(eye.Y / Units.CellSize, surface, terrain.CeilingHeight(eye.X / Units.CellSize, eye.Z / Units.CellSize)) * Units.CellSize
            : surface * Units.CellSize - eye.Y;
        _environment.Apply(Session.Clock, depth, skyExposure, delta);
    }

    /// <summary>
    /// The horizon fills the view beyond the streamed chunks; fog, aerial perspective and the camera's far plane
    /// reach out to it (or stop at the streamed edge when the preset has no horizon).
    /// </summary>
    private void UpdateDistance(Camera3D? camera, Vector3 eye, Int3 focus)
    {
        var s = Streamer.Settings;
        _horizon.Update(eye, ChunkMath.CellToChunk(focus), s.MeshRadius, s.MeshDown, s.MeshUp);
        float horizon = _services.Graphics.Preset.HorizonDistance;
        _environment.ViewDistance = Mathf.Max(_streamDistance, horizon * 0.85f);
        if (camera != null) camera.Far = Mathf.Max(1500f, horizon + 200f);
    }

    /// <summary>The highest-priority biome or zone at a cell (the crypt over the tundra, a hardmode wedge over forest).</summary>
    private Strata.Core.Content.Defs.BiomeDef? BiomeAround(Int3 cell)
    {
        Strata.Core.Content.Defs.BiomeDef? best = null;
        foreach (var id in Session.ContextAt(cell).Biomes)
            if (Session.Content.Biomes.TryGet(id, out var def) && (best == null || def.Priority > best.Priority)) best = def;
        return best;
    }

    /// <summary>Hide chunks the camera cannot see into (on by default; the <c>cull</c> command toggles it).</summary>
    public static bool CaveCulling { get; set; } = true;

    public override void _PhysicsProcess(double delta)
    {
        using var tick = Strata.Core.Diagnostics.Metrics.Frame.Time("tick.total");
        if (Simulating) Game.Tick();
    }

    public override void _ExitTree()
    {
        try
        {
            Game.Shutdown();
        }
        catch (System.Exception ex)
        {
            _services.Log.Error("world", "Saving on exit failed", ex);
        }
        Game.Dispose();
    }
}
