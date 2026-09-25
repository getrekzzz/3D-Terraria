using Strata.Core.Content;
using Strata.Core.Content.Loading;
using Strata.Core.Content.Validation;

namespace Strata.Tools.Commands;

/// <summary>Loads all content with strict validation and prints issues. Non-zero exit on errors.</summary>
public sealed class ValidateCommand : IToolCommand
{
    public string Name => "validate";
    public string Description => "Validate all content (references, specs, assets, progression DAG)";

    public int Run(ToolContext context)
    {
        var source = new FileSystemDataSource(context.ProjectRoot);
        var options = new ContentValidationOptions
        {
            AssetExists = context.Options.Flag("no-assets") ? null : p => File.Exists(context.PathOf(p)),
            TextureLayers = AssetCatalogs.LoadTextureLayers(context),
        };
        var db = ContentLoader.Load(source, options);
        foreach (var issue in db.Report.Issues.OrderByDescending(i => i.Severity))
            Console.WriteLine(issue);

        Console.WriteLine();
        foreach (var reg in db.AllRegistries) Console.WriteLine($"  {reg.Category,-12} {reg.Count,5}");
        int errors = db.Report.Errors.Count();
        int warnings = db.Report.Warnings.Count();
        Console.WriteLine($"{errors} error(s), {warnings} warning(s)");
        return errors == 0 ? 0 : 1;
    }
}

internal static class AssetCatalogs
{
    public const string TextureLayersPath = "assets/textures/terrain/layers.json";

    public static IReadOnlySet<string>? LoadTextureLayers(ToolContext context)
    {
        var path = context.PathOf(TextureLayersPath);
        if (!File.Exists(path)) return null;
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.GetProperty("layers").EnumerateArray().Select(e => e.GetProperty("name").GetString()!).ToHashSet();
    }
}
