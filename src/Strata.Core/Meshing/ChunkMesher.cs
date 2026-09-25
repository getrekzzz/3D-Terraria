using System.Diagnostics;
using Strata.Core.Common;
using Strata.Core.Lighting;

namespace Strata.Core.Meshing;

/// <summary>Everything produced for one chunk by a mesh job (engine-agnostic).</summary>
public sealed class ChunkMeshResult
{
    public required Int3 Coord { get; init; }

    /// <summary>Version of the centre chunk the mesh was built from (stale results are discarded).</summary>
    public required int Version { get; init; }

    public required int[] NeighbourVersions { get; init; }
    public MeshSurface Opaque { get; } = new();
    public MeshSurface Transparent { get; } = new();
    public MeshSurface Cutout { get; } = new();
    public MeshSurface Liquid { get; } = new();
    public CollisionBuilder Collision { get; } = new();

    /// <summary>Light of the centre chunk's cells (for gameplay queries).</summary>
    public required ushort[] Light { get; init; }

    public double BuildMs { get; set; }

    public bool IsEmpty => Opaque.IsEmpty && Transparent.IsEmpty && Cutout.IsEmpty && Liquid.IsEmpty;
}

/// <summary>Runs lighting and all meshers for one chunk. Thread-safe (per-thread scratch buffers).</summary>
public static class ChunkMesher
{
    [ThreadStatic] private static ushort[]? _light;

    public static ChunkMeshResult Build(Neighborhood nb, RenderTables tables, Func<int, int, int> skyTop, IReadOnlyList<LightSource> lightSources)
    {
        long start = Stopwatch.GetTimestamp();
        var light = LightEngine.Compute(nb, tables, skyTop, lightSources, _light ??= new ushort[Neighborhood.Volume]);
        var result = new ChunkMeshResult
        {
            Coord = nb.Chunk,
            Version = nb.Versions[13],
            NeighbourVersions = (int[])nb.Versions.Clone(),
            Light = ExtractCore(light),
        };
        var vol = new MeshVolume(nb, tables, light);
        SurfaceNetsMesher.Build(vol, result.Opaque, result.Collision);
        CellMesher.Build(vol, result.Opaque, result.Transparent, result.Cutout, result.Collision);
        LiquidMesher.Build(vol, result.Liquid);
        result.BuildMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        return result;
    }

    private static ushort[] ExtractCore(ushort[] light)
    {
        var core = new ushort[ChunkMath.Volume];
        for (int y = 0; y < ChunkMath.Size; y++)
            for (int z = 0; z < ChunkMath.Size; z++)
                Array.Copy(light, Neighborhood.Index(Neighborhood.Margin, y + Neighborhood.Margin, z + Neighborhood.Margin),
                    core, ChunkMath.Index(0, y, z), ChunkMath.Size);
        return core;
    }
}
