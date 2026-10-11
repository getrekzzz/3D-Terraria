namespace Strata.Core.WorldGen.Analysis;

/// <summary>
/// Aggregated terrain-shape metrics of a <see cref="SurfaceProfile"/> (see <see cref="SurfaceProfile.Stats"/>): how steep
/// the surface steps between neighbouring columns are, how rough it is, how often it overhangs, floats or breaks open,
/// and how much large-scale relief it keeps (a guard against smoothing that simply flattens the land).
/// </summary>
public sealed record TerrainStats
{
    /// <summary>Labels of <see cref="StepHistogram"/> buckets (absolute Top difference between 4-neighbours).</summary>
    public static readonly IReadOnlyList<string> StepBucketLabels = new[] { "0", "1", "2", "3", "4-7", "8+" };

    /// <summary>Blocks of this many cells square measure <see cref="Relief"/>.</summary>
    public const int ReliefBlock = 128;

    /// <summary>A relief block (or a biome in the breakdown) needs this many included columns to count.</summary>
    public const int MinColumns = 256;

    public int Columns { get; init; }

    /// <summary>Neighbour pairs (along x and z) with both columns included.</summary>
    public long StepPairs { get; init; }

    public IReadOnlyList<long> StepHistogram { get; init; } = new long[6];
    public int StepP95 { get; init; }
    public int StepP99 { get; init; }
    public int StepMax { get; init; }

    /// <summary>Percent of columns flagged <see cref="SurfaceFlags.Lip"/>.</summary>
    public double LipPercent { get; init; }

    /// <summary>Floating cells (<see cref="SurfaceProfile.FloatingCells"/>) per 10 000 columns.</summary>
    public double FloatingPer10k { get; init; }

    public double BreachPercent { get; init; }
    public double WaterPercent { get; init; }

    /// <summary>Mean |Top(x+1) + Top(x−1) − 2·Top(x)| over both axes (cells).</summary>
    public double Roughness { get; init; }

    /// <summary>Column-weighted mean standard deviation of Top inside <see cref="ReliefBlock"/>² blocks (cells).</summary>
    public double Relief { get; init; }

    /// <summary>Per surface biome id (biomes with at least <see cref="MinColumns"/> columns); empty for synthetic profiles.</summary>
    public IReadOnlyDictionary<string, TerrainStats> ByBiome { get; init; } = new Dictionary<string, TerrainStats>();

    internal static TerrainStats Compute(SurfaceProfile p, Func<int, int, bool>? include)
    {
        var mask = new bool[p.ColumnCount];
        for (int z = 0; z < p.Depth; z++)
            for (int x = 0; x < p.Width; x++)
                mask[z * p.Width + x] = include == null || include(p.X0 + x, p.Z0 + z);
        var stats = Compute(p, mask);
        if (p.Biome == null) return stats;

        var counts = new int[256];
        for (int i = 0; i < mask.Length; i++)
            if (mask[i]) counts[p.Biome[i]]++;
        var byBiome = new SortedDictionary<string, TerrainStats>(StringComparer.Ordinal);
        for (int b = 0; b < counts.Length; b++)
        {
            if (counts[b] < MinColumns) continue;
            var biomeMask = new bool[mask.Length];
            for (int i = 0; i < mask.Length; i++) biomeMask[i] = mask[i] && p.Biome[i] == b;
            byBiome[p.Content.Biomes[b].Id] = Compute(p, biomeMask);
        }
        return stats with { ByBiome = byBiome };
    }

    private static TerrainStats Compute(SurfaceProfile p, bool[] mask)
    {
        int w = p.Width, d = p.Depth;
        var top = p.Top;
        int columns = 0, lips = 0, breaches = 0, water = 0;
        long floating = 0;
        int minTop = int.MaxValue, maxTop = int.MinValue;
        for (int i = 0; i < mask.Length; i++)
        {
            if (!mask[i]) continue;
            columns++;
            var f = p.Flags[i];
            if ((f & SurfaceFlags.Lip) != 0) lips++;
            if ((f & SurfaceFlags.Breach) != 0) breaches++;
            if ((f & SurfaceFlags.Water) != 0) water++;
            floating += p.FloatingCells[i];
            minTop = Math.Min(minTop, top[i]);
            maxTop = Math.Max(maxTop, top[i]);
        }
        if (columns == 0) return new TerrainStats();

        var exact = new long[maxTop - minTop + 1];
        var buckets = new long[StepBucketLabels.Count];
        long pairs = 0, curvatureSamples = 0;
        double curvature = 0;
        for (int z = 0; z < d; z++)
            for (int x = 0; x < w; x++)
            {
                int i = z * w + x;
                if (!mask[i]) continue;
                if (x + 1 < w && mask[i + 1]) { AddStep(Math.Abs(top[i + 1] - top[i]), exact, buckets); pairs++; }
                if (z + 1 < d && mask[i + w]) { AddStep(Math.Abs(top[i + w] - top[i]), exact, buckets); pairs++; }
                if (x > 0 && x + 1 < w && mask[i - 1] && mask[i + 1]) { curvature += Math.Abs(top[i + 1] + top[i - 1] - 2 * top[i]); curvatureSamples++; }
                if (z > 0 && z + 1 < d && mask[i - w] && mask[i + w]) { curvature += Math.Abs(top[i + w] + top[i - w] - 2 * top[i]); curvatureSamples++; }
            }

        return new TerrainStats
        {
            Columns = columns,
            StepPairs = pairs,
            StepHistogram = buckets,
            StepP95 = Percentile(exact, pairs, 0.95),
            StepP99 = Percentile(exact, pairs, 0.99),
            StepMax = Percentile(exact, pairs, 1.0),
            LipPercent = Round(100.0 * lips / columns),
            FloatingPer10k = Round(10000.0 * floating / columns),
            BreachPercent = Round(100.0 * breaches / columns),
            WaterPercent = Round(100.0 * water / columns),
            Roughness = Round(curvatureSamples == 0 ? 0 : curvature / curvatureSamples),
            Relief = Round(BlockRelief(p, mask)),
        };
    }

    private static void AddStep(int step, long[] exact, long[] buckets)
    {
        exact[step]++;
        buckets[step switch { <= 3 => step, <= 7 => 4, _ => 5 }]++;
    }

    /// <summary>Smallest step value whose cumulative share reaches <paramref name="q"/> (0 without pairs).</summary>
    private static int Percentile(long[] exact, long total, double q)
    {
        if (total == 0) return 0;
        long need = (long)Math.Ceiling(q * total), seen = 0;
        for (int s = 0; s < exact.Length; s++)
            if ((seen += exact[s]) >= need) return s;
        return exact.Length - 1;
    }

    private static double BlockRelief(SurfaceProfile p, bool[] mask)
    {
        double weighted = 0;
        long weight = 0;
        for (int bz = 0; bz < p.Depth; bz += ReliefBlock)
            for (int bx = 0; bx < p.Width; bx += ReliefBlock)
            {
                long n = 0;
                double sum = 0, sumSq = 0;
                for (int z = bz; z < Math.Min(p.Depth, bz + ReliefBlock); z++)
                    for (int x = bx; x < Math.Min(p.Width, bx + ReliefBlock); x++)
                    {
                        int i = z * p.Width + x;
                        if (!mask[i]) continue;
                        double t = p.Top[i];
                        n++;
                        sum += t;
                        sumSq += t * t;
                    }
                if (n < MinColumns) continue;
                double mean = sum / n;
                weighted += Math.Sqrt(Math.Max(0, sumSq / n - mean * mean)) * n;
                weight += n;
            }
        return weight == 0 ? 0 : weighted / weight;
    }

    private static double Round(double v) => Math.Round(v, 4);
}
