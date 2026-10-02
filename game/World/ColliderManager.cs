using System.Collections.Generic;
using Godot;
using Strata.Core.Common;
using Strata.Core.Meshing;

namespace Strata.Game.World;

/// <summary>What <see cref="ColliderManager.SetData"/> did with a chunk's new collision data.</summary>
public enum ColliderChange
{
    /// <summary>Same geometry as the built bodies (liquid- or light-only remesh): no work at all.</summary>
    Unchanged,

    /// <summary>Some bucket changed; its body is rebuilt by <see cref="ColliderManager.UpdateFocus"/>.</summary>
    Pending,

    /// <summary>The chunk has no bodies yet; the data is kept until the player comes near.</summary>
    Stored,
}

/// <summary>
/// Static trimesh colliders for chunks near the physics focus only (the player). Collision data is kept for every
/// meshed chunk so bodies can be created when the player approaches. Each chunk has one static body per non-empty
/// collision bucket (8 solid and 8 platform sub-chunks of 16³ cells); a remesh rebuilds only the buckets whose geometry
/// hash changed, and the old body stays until its replacement exists, so the ground never has holes.
/// </summary>
public sealed partial class ColliderManager : Node3D
{
    public const uint TerrainLayer = 1;
    public const uint PlatformLayer = 2;

    /// <summary>Solid tile objects (closed doors, chests, tree trunks).</summary>
    public const uint ObjectLayer = 4;

    private const int AllBuckets = (1 << CollisionBuilder.HashCount) - 1;

    private sealed class ChunkCollision
    {
        public CollisionBuilder Data = null!;
        public ulong[] Hashes = null!;
        public readonly ulong[] BuiltHashes = new ulong[CollisionBuilder.HashCount];
        public readonly Rid[] Bodies = new Rid[CollisionBuilder.HashCount];
        public readonly Rid[] Shapes = new Rid[CollisionBuilder.HashCount];

        /// <summary>Buckets whose built body differs from <see cref="Hashes"/>.</summary>
        public int DirtyMask;

        /// <summary>Bodies exist for this chunk (possibly for slightly older data while a rebuild is pending).</summary>
        public bool Built;

        /// <summary>A player edit touched the chunk: its next rebuild goes before the distance-ordered ones.</summary>
        public bool Prioritized;
    }

    private readonly Dictionary<Int3, ChunkCollision> _chunks = new();
    private readonly HashSet<Int3> _built = new();
    private readonly List<(int Stage, int Distance, Int3 Coord)> _work = new();
    private readonly List<Int3> _scratch = new();

    /// <summary>Chunks (Chebyshev distance) around the focus that get physics bodies.</summary>
    public int Radius { get; set; } = 2;

    /// <summary>Chunks that currently have bodies (the <c>colliders.bodies</c> probe).</summary>
    public int BodyCount => _built.Count;

    /// <summary>Chunk builds (first builds and bucket rebuilds) since the last <see cref="ResetCounters"/>.</summary>
    public int Rebuilt { get; private set; }

    /// <summary>Remeshes whose collision geometry was unchanged (no work) since the last <see cref="ResetCounters"/>.</summary>
    public int Skipped { get; private set; }

    /// <summary>Chunks near the focus waiting for a build or rebuild after the last <see cref="UpdateFocus"/>.</summary>
    public int PendingCount { get; private set; }

    /// <summary>A chunk's terrain bodies were built or some bucket's geometry changed (resting objects can settle).</summary>
    public event System.Action<Int3>? BodyBuilt;

    /// <summary>Version counter per chunk, bumped whenever its collider is rebuilt (tests assert on it).</summary>
    public Dictionary<Int3, int> Versions { get; } = new();

    public void ResetCounters() => Rebuilt = Skipped = 0;

    public ColliderChange SetData(Int3 coord, CollisionBuilder data)
    {
        var hashes = data.Hashes ?? data.ComputeHashes();
        if (!_chunks.TryGetValue(coord, out var c)) _chunks[coord] = c = new ChunkCollision();
        if (!c.Built)
        {
            c.Data = data;
            c.Hashes = hashes;
            return ColliderChange.Stored;
        }
        int mask = 0;
        for (int i = 0; i < hashes.Length; i++)
            if (hashes[i] != c.BuiltHashes[i]) mask |= 1 << i;
        if (mask == 0)
        {
            // Keep the old data object: same triangles, and any pending mask stays valid against it.
            if (c.DirtyMask != 0)
            {
                c.Data = data;
                c.Hashes = hashes;
                c.DirtyMask = 0;
            }
            Skipped++;
            return ColliderChange.Unchanged;
        }
        c.Data = data;
        c.Hashes = hashes;
        c.DirtyMask = mask;
        return ColliderChange.Pending;
    }

    /// <summary>Player edits: these chunks' next rebuilds go first.</summary>
    public void Prioritize(IEnumerable<Int3> chunks)
    {
        foreach (var coord in chunks)
            if (_chunks.TryGetValue(coord, out var c)) c.Prioritized = true;
    }

    public void Remove(Int3 coord)
    {
        if (_chunks.Remove(coord, out var c)) FreeBodies(coord, c);
    }

    /// <summary>The chunk has had its bodies built near the player (empty chunks count: they need none).</summary>
    public bool HasBody(Int3 coord) => _chunks.TryGetValue(coord, out var c) && c.Built;

    /// <summary>The chunk is near enough for bodies but its build or rebuild has not run yet.</summary>
    public bool IsPending(Int3 coord) => _chunks.TryGetValue(coord, out var c) && (!c.Built || c.DirtyMask != 0);

    /// <summary>
    /// Keeps bodies for chunks near the focus. Frees far bodies, then builds (a) the focus chunk and never-built
    /// chunks touching it at once, (b) chunks edited by the player, (c) the rest nearest-first while the time budget
    /// lasts. Prioritized edits next to the focus are also built at once.
    /// </summary>
    public void UpdateFocus(Int3 focusChunk, double budgetMs = 2.0)
    {
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        FreeFar(focusChunk);

        _work.Clear();
        for (int dy = -Radius; dy <= Radius; dy++)
            for (int dz = -Radius; dz <= Radius; dz++)
                for (int dx = -Radius; dx <= Radius; dx++)
                {
                    var coord = focusChunk + new Int3(dx, dy, dz);
                    if (!_chunks.TryGetValue(coord, out var c) || (c.Built && c.DirtyMask == 0)) continue;
                    int distance = System.Math.Max(System.Math.Abs(dx), System.Math.Max(System.Math.Abs(dy), System.Math.Abs(dz)));
                    bool immediate = distance == 0 || (distance == 1 && (!c.Built || c.Prioritized));
                    _work.Add((immediate ? 0 : c.Prioritized ? 1 : 2, distance, coord));
                }
        _work.Sort(WorkComparer.Instance);
        PendingCount = _work.Count;
        foreach (var (stage, _, coord) in _work)
        {
            if (stage > 0 && System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds >= budgetMs) break;
            Build(coord, _chunks[coord]);
            PendingCount--;
        }
    }

    private void FreeFar(Int3 focusChunk)
    {
        _scratch.Clear();
        foreach (var coord in _built)
            if ((coord - focusChunk).ChebyshevLength > Radius) _scratch.Add(coord);
        foreach (var coord in _scratch) FreeBodies(coord, _chunks[coord]);
    }

    /// <summary>Stage, then distance, then X, Y, Z (deterministic order).</summary>
    private sealed class WorkComparer : IComparer<(int Stage, int Distance, Int3 Coord)>
    {
        public static readonly WorkComparer Instance = new();

        public int Compare((int Stage, int Distance, Int3 Coord) a, (int Stage, int Distance, Int3 Coord) b)
        {
            if (a.Stage != b.Stage) return a.Stage.CompareTo(b.Stage);
            if (a.Distance != b.Distance) return a.Distance.CompareTo(b.Distance);
            if (a.Coord.X != b.Coord.X) return a.Coord.X.CompareTo(b.Coord.X);
            if (a.Coord.Y != b.Coord.Y) return a.Coord.Y.CompareTo(b.Coord.Y);
            return a.Coord.Z.CompareTo(b.Coord.Z);
        }
    }

    public override void _ExitTree()
    {
        foreach (var (coord, c) in _chunks) FreeBodies(coord, c);
        _chunks.Clear();
    }
}
