using Godot;
using Strata.Core.Common;
using Strata.Core.Diagnostics;
using Strata.Core.Session;
using Strata.Core.Streaming;
using Strata.Game.Ambience;
using Strata.Game.Bootstrap;

namespace Strata.Game.World;

/// <summary>
/// Hosts a live <see cref="WorldSession"/> in the scene: streaming, job completion, mesh upload,
/// colliders, clock and environment. Gameplay nodes (player, entities) attach under it.
/// </summary>
public sealed partial class WorldRuntime : Node3D
{
    private GameServices _services = null!;
    private JobSystem _jobs = null!;
    private ChunkRenderer _renderer = null!;
    private ColliderManager _colliders = null!;
    private EnvironmentController _environment = null!;
    private int _tickAccumulator;

    public WorldSession Session { get; private set; } = null!;
    public ChunkStreamer Streamer { get; private set; } = null!;
    public ChunkRenderer Renderer => _renderer;
    public ColliderManager Colliders => _colliders;
    public EnvironmentController Environment => _environment;
    public JobSystem Jobs => _jobs;

    /// <summary>Node whose position drives streaming (player or camera).</summary>
    public Node3D? Focus { get; set; }

    public void Initialize(GameServices services, WorldSession session, StreamingSettings streaming)
    {
        _services = services;
        Session = session;
        _jobs = new JobSystem(System.Math.Max(1, System.Environment.ProcessorCount - 2), services.Log);
        Streamer = new ChunkStreamer(session, _jobs, streaming);

        _renderer = new ChunkRenderer { Name = "Chunks" };
        _renderer.Initialize(new TerrainMaterials(services.Render));
        AddChild(_renderer);
        _colliders = new ColliderManager { Name = "Colliders" };
        AddChild(_colliders);
        _environment = new EnvironmentController { Name = "Environment" };
        AddChild(_environment);

        Streamer.MeshReady += result =>
        {
            _renderer.Enqueue(result);
            _colliders.SetData(result.Coord, result.Collision);
        };
        Streamer.ChunkRemoved += coord =>
        {
            _renderer.Remove(coord);
            _colliders.Remove(coord);
        };
    }

    public Int3 FocusCell
    {
        get
        {
            var p = Focus?.GlobalPosition ?? Vector3.Zero;
            return new Int3(Mathf.FloorToInt(p.X / Units.CellSize), Mathf.FloorToInt(p.Y / Units.CellSize), Mathf.FloorToInt(p.Z / Units.CellSize));
        }
    }

    /// <summary>Applies an edit batch and schedules remeshing of every affected chunk.</summary>
    public void Commit(Strata.Core.Voxels.EditBatch batch) => Streamer.MarkDirty(Session.Commit(batch));

    public override void _Process(double delta)
    {
        var focus = FocusCell;
        Streamer.Update(focus);
        _jobs.Drain(4.0);
        _renderer.ProcessUploads(3.0);
        _colliders.UpdateFocus(ChunkMath.CellToChunk(focus));
        float surface = Session.Generator.Terrain.SurfaceHeight(focus.X, focus.Z);
        _environment.Apply(Session.Clock, (surface - focus.Y) * Units.CellSize, delta);
    }

    public override void _PhysicsProcess(double delta)
    {
        Session.Clock.Advance();
        if (++_tickAccumulator >= Units.TicksPerSecond * 120)
        {
            _tickAccumulator = 0;
            int saved = Session.SaveAll();
            _services.Log.Info("world", $"Autosaved ({saved} chunks).");
        }
    }

    public override void _ExitTree()
    {
        try
        {
            Session.SaveAll();
            Streamer.UnloadAll();
        }
        catch (System.Exception ex)
        {
            _services.Log.Error("world", "Saving on exit failed", ex);
        }
        _jobs.Dispose();
    }
}
