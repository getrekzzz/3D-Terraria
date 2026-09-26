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

    /// <summary>Node whose position drives streaming (player or camera).</summary>
    public Node3D? Focus { get; set; }

    /// <summary>When false the fixed tick is paused (menus, loading).</summary>
    public bool Simulating { get; set; } = true;

    public void Initialize(GameServices services, GameWorld game)
    {
        _services = services;
        Game = game;

        _renderer = new ChunkRenderer { Name = "Chunks" };
        _renderer.Initialize(new TerrainMaterials(services.Render));
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
        _environment.ViewDistance = game.Streamer.Settings.MeshRadius * ChunkMath.Size * Units.CellSize;
        AddChild(_environment);

        Streamer.MeshReady += result =>
        {
            _renderer.Enqueue(result);
            _grass?.Set(result.Coord, result.Grass);
            _colliders.SetData(result.Coord, result.Collision);
        };
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
        var focus = FocusCell;
        // Finish more background work per frame when it piles up (initial load, fast travel).
        int backlog = Jobs.Completions.Count;
        Game.Update(focus, backlog > 64 ? 14.0 : backlog > 16 ? 8.0 : 4.0);
        _renderer.ProcessUploads(3.0);
        _colliders.UpdateFocus(ChunkMath.CellToChunk(focus));
        _environment.EventTint = Game.Events.SkyTint is { } tint ? Color.FromHtml(tint) : null;
        if (Engine.GetProcessFrames() % 15 == 0) _environment.Biome = BiomeAround(focus);
        var camera = GetViewport().GetCamera3D();
        var eye = camera?.GlobalPosition ?? Focus?.GlobalPosition ?? Vector3.Zero;
        _grass?.Update(eye, Focus?.GlobalPosition ?? eye);
        float surface = Session.Generator.Terrain.SurfaceHeight(eye.X / Units.CellSize, eye.Z / Units.CellSize);
        float skyExposure = Rendering.LightProbe.Sample(Session.World, eye).A;
        _environment.Apply(Session.Clock, surface * Units.CellSize - eye.Y, skyExposure, delta);
    }

    /// <summary>The highest-priority biome or zone at a cell (the crypt over the tundra, a hardmode wedge over forest).</summary>
    private Strata.Core.Content.Defs.BiomeDef? BiomeAround(Int3 cell)
    {
        Strata.Core.Content.Defs.BiomeDef? best = null;
        foreach (var id in Session.ContextAt(cell).Biomes)
            if (Session.Content.Biomes.TryGet(id, out var def) && (best == null || def.Priority > best.Priority)) best = def;
        return best;
    }

    public override void _PhysicsProcess(double delta)
    {
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
