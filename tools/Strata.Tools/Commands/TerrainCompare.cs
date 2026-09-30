using System.Globalization;
using System.Text.Json;
using Strata.Core.WorldGen.Analysis;

namespace Strata.Tools.Commands;

/// <summary>
/// Before/after table of the <c>terrain</c> section of two worldgen-report stats.json files (--compare): the evidence
/// that a terrain change smoothed the surface without flattening it.
/// </summary>
internal static class TerrainCompare
{
    private static readonly string[] Metrics =
        { "columns", "lipPercent", "floatingPer10k", "breachPercent", "waterPercent", "stepP95", "stepP99", "stepMax", "roughness", "relief" };

    private static readonly string[] BiomeMetrics = { "columns", "lipPercent", "floatingPer10k", "breachPercent", "stepP99", "roughness", "relief" };

    /// <summary>Prints the table and writes it to <paramref name="outPath"/>; false when the older file has no terrain section.</summary>
    public static bool Write(string beforePath, string afterJson, string outPath)
    {
        if (!File.Exists(beforePath))
        {
            Console.Error.WriteLine($"--compare: {beforePath} not found");
            return false;
        }
        using var beforeDoc = JsonDocument.Parse(File.ReadAllText(beforePath));
        using var afterDoc = JsonDocument.Parse(afterJson);
        if (!beforeDoc.RootElement.TryGetProperty("terrain", out var before))
        {
            Console.Error.WriteLine($"--compare: {beforePath} has no terrain section (made before terrain metrics existed)");
            return false;
        }
        var after = afterDoc.RootElement.GetProperty("terrain");
        var lines = new List<string>();
        foreach (var key in new[] { "seed", "size" })
            if (Text(beforeDoc.RootElement, key) != Text(afterDoc.RootElement, key))
                lines.Add($"warning: {key} differs ({Text(beforeDoc.RootElement, key)} vs {Text(afterDoc.RootElement, key)})");
        if (Window(before) != Window(after)) lines.Add($"warning: window differs ({Window(before)} vs {Window(after)})");
        lines.Add($"{"terrain metric",-34} {"before",12} {"after",12} {"delta",12}");

        foreach (var m in Metrics) lines.Add(Row(m, Number(before, m), Number(after, m)));
        for (int b = 0; b < TerrainStats.StepBucketLabels.Count; b++)
            lines.Add(Row($"step {TerrainStats.StepBucketLabels[b]} (% of pairs)", BucketPercent(before, b), BucketPercent(after, b)));

        var biomes = Biomes(before).Union(Biomes(after)).OrderBy(b => b, StringComparer.Ordinal);
        foreach (var biome in biomes)
        {
            var bb = Biome(before, biome);
            var ab = Biome(after, biome);
            foreach (var m in BiomeMetrics) lines.Add(Row($"{biome}.{m}", bb is { } x ? Number(x, m) : null, ab is { } y ? Number(y, m) : null));
        }

        foreach (var line in lines) Console.WriteLine(line);
        File.WriteAllLines(outPath, lines);
        return true;
    }

    private static string Row(string name, double? before, double? after)
    {
        string delta = before is { } b && after is { } a ? (a - b).ToString("+0.####;-0.####;0", CultureInfo.InvariantCulture) : "-";
        return $"{name,-34} {Format(before),12} {Format(after),12} {delta,12}";
    }

    private static string Format(double? v) => v?.ToString("0.####", CultureInfo.InvariantCulture) ?? "-";

    private static double? Number(JsonElement e, string key) =>
        e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;

    private static string Text(JsonElement e, string key) => e.TryGetProperty(key, out var v) ? v.GetRawText() : "";

    private static string Window(JsonElement stats) =>
        stats.TryGetProperty("window", out var w) ? $"{Number(w, "x0")},{Number(w, "z0")} {Number(w, "width")}x{Number(w, "depth")}" : "?";

    private static double? BucketPercent(JsonElement stats, int bucket)
    {
        if (!stats.TryGetProperty("stepHistogram", out var h) || Number(stats, "stepPairs") is not { } pairs || pairs == 0) return null;
        return Math.Round(100.0 * h[bucket].GetDouble() / pairs, 3);
    }

    private static IEnumerable<string> Biomes(JsonElement stats) =>
        stats.TryGetProperty("byBiome", out var b) ? b.EnumerateObject().Select(p => p.Name).ToList() : Enumerable.Empty<string>();

    private static JsonElement? Biome(JsonElement stats, string biome) =>
        stats.TryGetProperty("byBiome", out var b) && b.TryGetProperty(biome, out var s) ? s : null;
}
