using Strata.Core.Common;
using Strata.Core.Voxels;

namespace Strata.Core.WorldGen.Analysis;

public sealed partial class SurfaceProfile
{
    /// <summary>
    /// Generates the chunks a window's surface can occupy and profiles them. Per chunk column the surface bounds are
    /// sampled every 4 cells (like <see cref="Plan.WorldPlan.RaiseTerrainColumnTops"/>) and only the chunks from
    /// surface + <see cref="Headroom"/> down to surface − <see cref="Footroom"/> are generated; the window is clamped to
    /// the world. Chunk columns are processed independently (in parallel when <paramref name="threads"/> &gt; 1), so
    /// memory stays bounded for large windows.
    /// </summary>
    public static SurfaceProfile Measure(WorldGenerator gen, int x0, int z0, int width, int depth, int threads = 1)
    {
        var size = gen.Plan.Size;
        int xa = Math.Clamp(x0, 0, size.X), za = Math.Clamp(z0, 0, size.Z);
        int xb = Math.Clamp(x0 + width, 0, size.X), zb = Math.Clamp(z0 + depth, 0, size.Z);
        if (xb <= xa || zb <= za) throw new ArgumentException("window lies outside the world");

        var profile = new SurfaceProfile(gen.Content, xa, za, xb - xa, zb - za) { Biome = new byte[(xb - xa) * (zb - za)] };
        var solid = SolidTable(gen.Content);
        int cx0 = ChunkMath.ToChunk(xa), cz0 = ChunkMath.ToChunk(za);
        int ncx = ChunkMath.ToChunk(xb - 1) - cx0 + 1, ncz = ChunkMath.ToChunk(zb - 1) - cz0 + 1;
        int generated = 0;

        void Column(int k)
        {
            int cx = cx0 + k % ncx, cz = cz0 + k / ncx;
            var (cyMin, cyMax) = ChunkRange(gen, cx, cz);
            var column = new ChunkData?[cyMax - cyMin + 1];
            for (int cy = cyMin; cy <= cyMax; cy++) column[cy - cyMin] = gen.Generate(new Int3(cx, cy, cz)).Data;
            Interlocked.Add(ref generated, column.Length);
            var reader = new ColumnReader(column, cyMin, solid);
            profile.ScanChunkColumn(cx, cz, reader, cyMin * ChunkMath.Size, (cyMax + 1) * ChunkMath.Size - 1, gen.Terrain.SurfaceHeight);
            profile.FillBiomes(gen, cx, cz);
        }

        int count = ncx * ncz;
        if (threads > 1) Parallel.For(0, count, new ParallelOptions { MaxDegreeOfParallelism = threads }, Column);
        else for (int k = 0; k < count; k++) Column(k);
        profile.ChunksGenerated = generated;
        return profile;
    }

    /// <summary>Chunk rows [min, max] that can hold the scan band of chunk column (cx, cz).</summary>
    private static (int Min, int Max) ChunkRange(WorldGenerator gen, int cx, int cz)
    {
        float lo = float.MaxValue, hi = float.MinValue;
        for (int z = -4; z <= ChunkMath.Size + 4; z += 4)
            for (int x = -4; x <= ChunkMath.Size + 4; x += 4)
            {
                float s = gen.Terrain.SurfaceHeight(cx * ChunkMath.Size + x, cz * ChunkMath.Size + z);
                lo = MathF.Min(lo, s);
                hi = MathF.Max(hi, s);
            }
        int top = gen.Plan.Size.Y / ChunkMath.Size - 1;
        int max = Math.Clamp(ChunkMath.ToChunk((int)MathF.Ceiling(hi) + Headroom + 4), 0, top);
        int min = Math.Clamp(ChunkMath.ToChunk((int)MathF.Floor(lo) - Footroom - 4), 0, max);
        return (min, max);
    }

    private void FillBiomes(WorldGenerator gen, int cx, int cz)
    {
        int xa = Math.Max(X0, cx << ChunkMath.Shift), xb = Math.Min(X0 + Width, (cx + 1) << ChunkMath.Shift);
        int za = Math.Max(Z0, cz << ChunkMath.Shift), zb = Math.Min(Z0 + Depth, (cz + 1) << ChunkMath.Shift);
        for (int z = za; z < zb; z++)
            for (int x = xa; x < xb; x++)
                Biome![Index(x, z)] = gen.Biomes.SurfaceBiome(x, z);
    }
}
