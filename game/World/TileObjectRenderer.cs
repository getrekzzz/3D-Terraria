using System.Collections.Generic;
using Godot;
using Strata.Core.Common;
using Strata.Core.Session;
using Strata.Core.Voxels.TileObjects;
using Strata.Game.Rendering;

namespace Strata.Game.World;

/// <summary>
/// Draws tile objects (trees, furniture, stations, flora) with one MultiMesh per chunk and model variant.
/// Chunks are updated in place: a structural change (objects added or removed, a door toggled) regroups the chunk,
/// a terrain change re-places its objects once the colliders they rest on are rebuilt, and a light-only remesh just
/// rewrites the light of the existing instances. Work is spread over frames within <see cref="WorldRuntime.Budgets"/>,
/// nearest chunks first.
/// </summary>
public sealed partial class TileObjectRenderer : Node3D
{
    /// <summary>What a chunk needs, from cheapest to most expensive (a larger kind includes the smaller ones).</summary>
    [System.Flags]
    private enum DirtyKind : byte
    {
        None = 0,
        Light = 1,
        Placement = 2,
        Structure = 4,
    }

    private readonly Dictionary<Int3, DirtyKind> _dirty = new();
    private readonly List<(long Distance, Int3 Coord)> _order = new();
    private GameWorld _game = null!;
    private WorldRuntime _runtime = null!;
    private PropLibrary _props = null!;
    private Strata.Core.Streaming.ChunkStreamer _streamer = null!;

    /// <summary>Where each object's model goes (grounding, wall mounting, doors).</summary>
    public ObjectPlacement Placement { get; private set; } = null!;

    /// <summary>Drawn object instances plus their flames.</summary>
    public int InstanceCount { get; private set; }

    /// <summary>Chunk regroups and re-placements since the last <see cref="ResetCounters"/>.</summary>
    public int Rebuilds { get; private set; }

    /// <summary>Light-only refreshes (instance light rewritten in place) since the last <see cref="ResetCounters"/>.</summary>
    public int LightRefreshes { get; private set; }

    /// <summary>Chunks waiting for an update.</summary>
    public int PendingCount => _dirty.Count;

    public void ResetCounters() => Rebuilds = LightRefreshes = 0;

    private ChunkRenderer? _chunkRenderer;

    public void Initialize(WorldRuntime runtime, PropLibrary props)
    {
        _game = runtime.Game;
        _runtime = runtime;
        _props = props;
        _streamer = runtime.Streamer;
        Placement = new ObjectPlacement(runtime);
        Name = "TileObjects";
        var store = _game.Session.World.TileObjects;
        store.Added += o => Mark(ChunkOf(o), DirtyKind.Structure);
        store.Removed += o =>
        {
            Mark(ChunkOf(o), DirtyKind.Structure);
            ForgetLid(o);
        };
        _game.Interaction.StateChanged += o => Mark(ChunkOf(o), DirtyKind.Structure);
        // A remesh whose collision changed waits for the new bodies (placement rays hit them), so the chunk is
        // re-placed once; one with the same collision only changed light (or liquids).
        runtime.MeshApplied += (c, change) =>
        {
            if (change == ColliderChange.Unchanged) Mark(c, DirtyKind.Light);
            else if (change == ColliderChange.Stored) Mark(c, DirtyKind.Placement);
        };
        runtime.Colliders.BodyBuilt += c => Mark(c, DirtyKind.Placement);
        _chunkRenderer = runtime.Renderer;
        runtime.Renderer.ChunkShown += (c, show) =>
        {
            if (_chunks.TryGetValue(c, out var chunk)) SetVisible(chunk, show);
        };
        runtime.Streamer.ChunkRemoved += c =>
        {
            _dirty.Remove(c);
            Clear(c);
        };
    }

    private void Mark(Int3 chunk, DirtyKind kind)
    {
        _dirty.TryGetValue(chunk, out var had);
        _dirty[chunk] = had | kind;
    }

    public override void _Process(double delta)
    {
        AnimateLids(delta);
        if (_dirty.Count == 0) return;
        using var profile = Strata.Core.Diagnostics.Metrics.Frame.Time("gd.objects");
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        double budgetMs = _runtime.Budgets.ObjectsMs;
        var camera = GetViewport().GetCamera3D();
        var eye = camera?.GlobalPosition ?? _runtime.Focus?.GlobalPosition ?? Vector3.Zero;
        var eyeChunk = ChunkMath.CellToChunk(new Int3(Mathf.FloorToInt(eye.X / Units.CellSize), Mathf.FloorToInt(eye.Y / Units.CellSize), Mathf.FloorToInt(eye.Z / Units.CellSize)));
        _order.Clear();
        foreach (var c in _dirty.Keys)
        {
            var d = c - eyeChunk;
            _order.Add(((long)d.X * d.X + (long)d.Y * d.Y + (long)d.Z * d.Z, c));
        }
        _order.Sort(OrderComparer.Instance);
        bool first = true;
        foreach (var (_, c) in _order)
        {
            // At least one chunk per frame, then as many as the budget allows.
            if (!first && System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds >= budgetMs) break;
            first = false;
            var kind = _dirty[c];
            _dirty.Remove(c);
            Update(c, kind);
        }
    }

    private static Int3 ChunkOf(TileObjectInstance o) => ChunkMath.CellToChunk(o.Anchor);

    /// <summary>Nearest first, then X, Y, Z (deterministic order).</summary>
    private sealed class OrderComparer : IComparer<(long Distance, Int3 Coord)>
    {
        public static readonly OrderComparer Instance = new();

        public int Compare((long Distance, Int3 Coord) a, (long Distance, Int3 Coord) b)
        {
            if (a.Distance != b.Distance) return a.Distance.CompareTo(b.Distance);
            if (a.Coord.X != b.Coord.X) return a.Coord.X.CompareTo(b.Coord.X);
            if (a.Coord.Y != b.Coord.Y) return a.Coord.Y.CompareTo(b.Coord.Y);
            return a.Coord.Z.CompareTo(b.Coord.Z);
        }
    }
}
