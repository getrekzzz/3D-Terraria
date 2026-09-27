using Strata.Core.Content.Loading;
using Strata.Core.Content.Reference;

namespace Strata.Tools.Commands;

/// <summary>Reports how much of the reference game's concepts the content covers (from content "covers" links).</summary>
public sealed class CoverageCommand : IToolCommand
{
    public string Name => "coverage";
    public string Description => "Coverage of data/reference concepts by content (--missing lists gaps, --out=file writes markdown)";

    public int Run(ToolContext context)
    {
        var source = new FileSystemDataSource(context.ProjectRoot);
        var content = ContentLoader.Load(source);
        var catalog = ReferenceCatalog.Load(source);
        var unknown = ReferenceCatalog.Covered(content).Keys.Where(id => !catalog.Contains(id)).OrderBy(i => i).ToList();
        var lines = catalog.Coverage(content);
        var report = new List<string> { "| Category | Covered | Total | % |", "|---|---:|---:|---:|" };
        foreach (var l in lines) report.Add($"| {l.Category} | {l.Covered} | {l.Total} | {l.Percent:0.0} |");
        int covered = lines.Sum(l => l.Covered), total = lines.Sum(l => l.Total);
        report.Add($"| **all** | **{covered}** | **{total}** | **{100.0 * covered / total:0.0}** |");
        foreach (var r in report) Console.WriteLine(r);
        if (context.Options.Flag("missing"))
            foreach (var l in lines)
                Console.WriteLine($"\n{l.Category} missing ({l.Missing.Count}): " + string.Join(", ", l.Missing.Select(m => m.Name)));
        if (context.Options.Get("out") is { } outPath) File.WriteAllLines(context.PathOf(outPath), report);
        foreach (var id in unknown) Console.WriteLine($"ERROR: content covers unknown concept '{id}'");
        return unknown.Count == 0 ? 0 : 1;
    }
}
