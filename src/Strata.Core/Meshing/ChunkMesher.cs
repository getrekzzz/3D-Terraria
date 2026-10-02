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

    /// <summary>Lava, drawn opaque with its own material (it hides what is behind it and needs no refraction).</summary>
    public MeshSurface Lava { get; } = new();

    /// <summary>World cells whose lava surface is open to the air (embers, heat light), at most <see cref="LiquidMesher.MaxLavaTops"/>.</summary>
    public List<Int3> LavaTops { get; } = new();
    public CollisionBuilder Collision { get; } = new();

    /// <summary>Grass clumps on the chunk's grass surfaces (empty when the tables carry no grass settings).</summary>
    public List<GrassClump> Grass { get; } = new();

    /// <summary>See <see cref="GrassScatter.Hashes"/>: equal shape hashes mean the same clumps, maybe relit.</summary>
    public ulong GrassShapeHash { get; set; }

    public ulong GrassLightHash { get; set; }

    /// <summary>Light of the centre chunk's cells (for gameplay queries).</summary>
    public required ushort[] Light { get; init; }

    public double BuildMs { get; set; }

    /// <summary>
    /// Some cell in the chunk or the one-cell border around it sees the sky. Chunks sealed away from it cannot throw
    /// a sun shadow onto anything the sun lights, so they skip the shadow passes.
    /// </summary>
    public bool SkyExposed { get; set; } = true;

    /// <summary>Which of the chunk's faces see each other through open cells (for cave culling).</summary>
    public ushort Visibility { get; set; } = ChunkVisibility.AllConnected;

    public bool IsEmpty => Opaque.IsEmpty && Transparent.IsEmpty && Cutout.IsEmpty && Liquid.IsEmpty && Lava.IsEmpty;
}

/// <summary>Runs lighting and all meshers for one chunk. Thread-safe (per-thread scratch buffers).</summary>
public static class ChunkMesher
{
    [ThreadStatic] private static ushort[]? _light;

    public static ChunkMeshResult Build(Neighborhood nb, RenderTables tables, Func<int, int, int> skyTop, IReadOnlyList<LightSource> lightSources,
        Func<int, int, int>? shadeTop = null)
    {
        long start = Stopwatch.GetTimestamp();
        var light = LightEngine.Compute(nb, tables, skyTop, lightSources, _light ??= new ushort[Neighborhood.Volume], shadeTop);
        var result = new ChunkMeshResult
        {
            Coord = nb.Chunk,
            Version = nb.Versions[13],
            NeighbourVersions = (int[])nb.Versions.Clone(),
            Light = ExtractCore(light),
        };
        var vol = MeshVolume.Rent(nb, tables, light);
        SurfaceNetsMesher.Build(vol, result.Opaque, result.Collision);
        CellMesher.Build(vol, result.Opaque, result.Transparent, result.Cutout, result.Collision);
        result.Collision.ComputeHashes();
        LiquidMesher.Build(vol, result.Liquid, result.Lava, result.LavaTops);
        GrassScatter.Build(vol, result.Opaque, result.Grass);
        (result.GrassShapeHash, result.GrassLightHash) = GrassScatter.Hashes(result.Grass);
        result.Visibility = ChunkVisibility.Compute(nb, tables);
        result.SkyExposed = AnySky(light);
        result.BuildMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        return result;
    }

    private static bool AnySky(ushort[] light)
    {
        const int lo = Neighborhood.Margin - 1, hi = Neighborhood.Margin + ChunkMath.Size;
        for (int y = lo; y <= hi; y++)
            for (int z = lo; z <= hi; z++)
            {
                int row = Neighborhood.Index(lo, y, z);
                for (int x = 0; x <= hi - lo; x++)
                    if (light[row + x] >= 1 << 12) return true;
            }
        return false;
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
