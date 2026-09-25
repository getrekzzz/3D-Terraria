using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Strata.Core.Common;
using Strata.Core.Content.Loading;
using Strata.Core.Diagnostics;
using Strata.Core.World;
using Strata.Core.WorldGen;
using Strata.Core.WorldGen.Chunk;

namespace Strata.Tools.Commands;

/// <summary>
/// Generates a world plan plus sample chunks and renders PNG maps (biomes, relief, vertical sections,
/// horizontal depth slices) and a stats JSON — the primary visual QA tool for world generation.
/// </summary>
public sealed class WorldgenReportCommand : IToolCommand
{
    public string Name => "worldgen-report";
    public string Description => "Render worldgen maps/sections + stats (--seed N --size tiny|small|medium --out dir)";

    private static int Threads = Environment.ProcessorCount;

    public int Run(ToolContext context)
    {
        Threads = context.Options.Int("threads", Environment.ProcessorCount);
        var content = ContentLoader.Load(new FileSystemDataSource(context.ProjectRoot));
        if (content.Report.HasErrors)
        {
            Console.Error.WriteLine(content.Report);
            return 1;
        }
        var settings = new WorldSettings
        {
            Seed = context.Options.Long("seed", 1234),
            Size = Enum.Parse<WorldSizePreset>(context.Options.Get("size") ?? "tiny", ignoreCase: true),
            Evil = context.Options.Get("evil") == "sanguine" ? EvilKind.Sanguine : EvilKind.Umbral,
        };
        var outDir = Path.GetFullPath(context.Options.Get("out") ?? context.PathOf("build/worldgen"));
        Directory.CreateDirectory(outDir);

        var sw = Stopwatch.StartNew();
        var gen = new WorldGenerator(content, settings, new ConsoleLog()) { ProfilePasses = new Metrics() };
        long planMs = sw.ElapsedMilliseconds;
        var painter = new ReportPainter(gen);
        painter.BiomeMap(Path.Combine(outDir, "biome_map.png"));
        painter.ReliefMap(Path.Combine(outDir, "relief_map.png"));

        var timings = new ConcurrentBag<double>();
        var stats = new ReportStats(gen);
        var spawn = gen.Plan.Spawn;
        var size = gen.Plan.Size;
        var counts = settings.ChunkCounts;

        var sectionX = Generate(gen, Enumerable.Range(0, counts.X).SelectMany(cx => Enumerable.Range(0, counts.Y).Select(cy => new Int3(cx, cy, spawn.Z >> 5))), timings, stats);
        painter.Section(Path.Combine(outDir, "section_x.png"), sectionX, alongX: true, fixedCoord: spawn.Z);
        var sectionZ = Generate(gen, Enumerable.Range(0, counts.Z).SelectMany(cz => Enumerable.Range(0, counts.Y).Select(cy => new Int3(spawn.X >> 5, cy, cz))), timings, stats);
        painter.Section(Path.Combine(outDir, "section_z.png"), sectionZ, alongX: false, fixedCoord: spawn.X);

        int window = Math.Min(size.X, context.Options.Int("window", 768)) / 32;
        foreach (var depth in new[] { 0.15f, 0.5f, 0.85f })
        {
            int y = (int)gen.Plan.Bands.YForNormalizedDepth(depth);
            int cx0 = Math.Max(0, (spawn.X >> 5) - window / 2), cz0 = Math.Max(0, (spawn.Z >> 5) - window / 2);
            var coords = Enumerable.Range(cx0, Math.Min(window, counts.X - cx0))
                .SelectMany(cx => Enumerable.Range(cz0, Math.Min(window, counts.Z - cz0)).Select(cz => new Int3(cx, y >> 5, cz)));
            var slice = Generate(gen, coords, timings, stats);
            painter.Slice(Path.Combine(outDir, $"slice_depth_{(int)(depth * 100):00}.png"), slice, y);
        }

        var summary = stats.Summarize(gen, planMs, timings);
        File.WriteAllText(Path.Combine(outDir, "stats.json"), JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));
        foreach (var (name, (count, total, max)) in gen.ProfilePasses!.SnapshotTimers().OrderByDescending(kv => kv.Value.TotalMs))
            Console.WriteLine($"  {name,-20} avg {total / count:F2} ms  max {max:F1} ms");
        Console.WriteLine($"plan {planMs} ms; {timings.Count} chunks, avg {timings.Average():F2} ms, max {timings.Max():F1} ms");
        Console.WriteLine($"report written to {outDir}");
        return 0;
    }

    private static Dictionary<Int3, GeneratedChunk> Generate(WorldGenerator gen, IEnumerable<Int3> coords, ConcurrentBag<double> timings, ReportStats stats)
    {
        var result = new ConcurrentDictionary<Int3, GeneratedChunk>();
        Parallel.ForEach(coords.ToList(), new ParallelOptions { MaxDegreeOfParallelism = Threads }, c =>
        {
            var t = Stopwatch.GetTimestamp();
            var chunk = gen.Generate(c);
            timings.Add(Stopwatch.GetElapsedTime(t).TotalMilliseconds);
            result[c] = chunk;
            stats.Add(chunk);
        });
        return new Dictionary<Int3, GeneratedChunk>(result);
    }
}
