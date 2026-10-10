using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Strata.Core.Common;
using Strata.Core.Content.Loading;
using Strata.Core.Diagnostics;
using Strata.Core.World;
using Strata.Core.WorldGen;
using Strata.Core.WorldGen.Analysis;
using Strata.Core.WorldGen.Chunk;

namespace Strata.Tools.Commands;

/// <summary>
/// Generates a world plan plus sample chunks and renders PNG maps (biomes, relief, vertical sections,
/// horizontal depth slices, terrain hillshade and flags) and a stats JSON — the primary visual QA tool for world
/// generation. <c>--compare=old/stats.json</c> prints the terrain metrics before/after (evidence for terrain changes).
/// </summary>
public sealed class WorldgenReportCommand : IToolCommand
{
    public string Name => "worldgen-report";
    public string Description => "Render worldgen maps/sections + stats (--seed N --size tiny|small|medium --out dir --terrain-window N --cave-window N --compare old/stats.json)";

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
            Evil = context.Options.Get("evil") == "crimson" ? EvilKind.Crimson : EvilKind.Corruption,
            Secret = SecretSeeds.Parse(context.Options.Get("secret") ?? ""),
        };
        var outDir = Path.GetFullPath(context.Options.Get("out") ?? context.PathOf("build/worldgen"));
        int terrainWindow = context.Options.Int("terrain-window", 512);
        string? compare = context.Options.Get("compare");
        if (compare != null && terrainWindow <= 0)
        {
            Console.Error.WriteLine("--compare needs the terrain metrics (--terrain-window > 0)");
            return 1;
        }
        // Read before anything is written: --compare may point at the stats.json this run replaces.
        string? beforeJson = compare != null ? TerrainCompare.Load(Path.GetFullPath(compare)) : null;
        if (compare != null && beforeJson == null) return 1;
        Directory.CreateDirectory(outDir);
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(outDir)!, ".gdignore"), "");

        var sw = Stopwatch.StartNew();
        var gen = new WorldGenerator(content, settings, new ConsoleLog()) { ProfilePasses = new Metrics() };
        long planMs = sw.ElapsedMilliseconds;
        var painter = new ReportPainter(gen);
        painter.BiomeMap(Path.Combine(outDir, "biome_map.png"));
        painter.ReliefMap(Path.Combine(outDir, "relief_map.png"));
        var network = CaveNetworkPainter.Paint(gen, Path.Combine(outDir, "cave_network.png"));
        var rooms = CaveRoomSheet.Paint(gen, painter, Path.Combine(outDir, "rooms"));

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

        var terrain = terrainWindow > 0 ? MeasureTerrain(gen, terrainWindow, outDir) : null;
        int caveWindow = context.Options.Int("cave-window", 0);
        var caves = caveWindow > 0 && !gen.Plan.Bands.Inverted ? MeasureCaves(gen, caveWindow, context.Options.Int("cave-offset", 0)) : null;

        var summary = stats.Summarize(gen, planMs, timings, terrain, caves, network, rooms);
        var json = JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(outDir, "stats.json"), json);
        foreach (var (name, (count, total, max)) in gen.ProfilePasses!.SnapshotTimers().OrderByDescending(kv => kv.Value.TotalMs))
            Console.WriteLine($"  {name,-20} avg {total / count:F2} ms  max {max:F1} ms");
        Console.WriteLine($"plan {planMs} ms; {timings.Count} chunks, avg {timings.Average():F2} ms, max {timings.Max():F1} ms");
        if (beforeJson != null) TerrainCompare.Write(beforeJson, json, Path.Combine(outDir, "terrain_compare.txt"));
        Console.WriteLine($"report written to {outDir}");
        return 0;
    }

    /// <summary>Profiles the surface in a square window centred on spawn (clamped to the world) and paints its maps.</summary>
    private static JsonObject MeasureTerrain(WorldGenerator gen, int window, string outDir)
    {
        var size = gen.Plan.Size;
        var spawn = gen.Plan.Spawn;
        int w = Math.Min(window, size.X), d = Math.Min(window, size.Z);
        int x0 = Math.Clamp(spawn.X - w / 2, 0, size.X - w), z0 = Math.Clamp(spawn.Z - d / 2, 0, size.Z - d);
        // Keep the per-pass timings printed at the end about the section/slice sample chunks only.
        var passTimers = gen.ProfilePasses;
        gen.ProfilePasses = null;
        var sw = Stopwatch.StartNew();
        var profile = SurfaceProfile.Measure(gen, x0, z0, w, d, Threads);
        gen.ProfilePasses = passTimers;
        var t = profile.Stats();
        long ms = sw.ElapsedMilliseconds;
        var painter = new TerrainPainter(gen, profile);
        painter.Hillshade(Path.Combine(outDir, "terrain_hillshade.png"));
        painter.FlagMap(Path.Combine(outDir, "terrain_flags.png"));
        Console.WriteLine($"terrain {w}x{d} at ({x0},{z0}): {profile.ChunksGenerated} chunks in {ms} ms; lip {t.LipPercent}% breach {t.BreachPercent}% "
            + $"floating {t.FloatingPer10k}/10k p99 step {t.StepP99} roughness {t.Roughness} relief {t.Relief}");

        var node = new JsonObject
        {
            ["window"] = new JsonObject { ["x0"] = x0, ["z0"] = z0, ["width"] = w, ["depth"] = d },
            ["chunks"] = profile.ChunksGenerated,
            ["measureMs"] = ms,
        };
        var statsNode = JsonSerializer.SerializeToNode(t, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })!.AsObject();
        foreach (var key in statsNode.Select(kv => kv.Key).ToList())
        {
            var value = statsNode[key];
            statsNode.Remove(key);
            node[key] = value;
        }
        return node;
    }

    /// <summary>Cave census of a square window, its centre <paramref name="offset"/> cells east of spawn (clamped to the world).</summary>
    private static CaveStats MeasureCaves(WorldGenerator gen, int window, int offset)
    {
        var size = gen.Plan.Size;
        var spawn = gen.Plan.Spawn;
        int w = Math.Min(window, size.X), d = Math.Min(window, size.Z);
        int x0 = Math.Clamp(spawn.X + offset - w / 2, 0, size.X - w), z0 = Math.Clamp(spawn.Z - d / 2, 0, size.Z - d);
        var passTimers = gen.ProfilePasses;
        gen.ProfilePasses = null;
        var sw = Stopwatch.StartNew();
        var caves = CaveProfile.Measure(gen, x0, z0, w, d, Threads);
        gen.ProfilePasses = passTimers;
        Console.WriteLine($"caves {w}x{d} at ({x0},{z0}): {caves.Chunks} chunks in {sw.ElapsedMilliseconds} ms; open "
            + string.Join(" ", caves.OpenPercent.Select(kv => $"{kv.Key} {kv.Value}%")) + $"; sky-linked {caves.SkyLinkedPercent}% largest {caves.LargestPiecePercent}% "
            + $"pockets {caves.PocketsPerMillion}/M ({caves.PocketPercent}%) floor {caves.FloorPercent}% hall floor {caves.HallFloorPercent}% cramped {caves.CrampedPercent}%");
        return caves;
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
