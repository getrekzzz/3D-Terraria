using Strata.Core.Content;
using Strata.Core.Content.Loading;
using Strata.Core.Content.Validation;
using Strata.Core.Presentation;

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
        // Engine-side configuration that is not part of the content database.
        errors += ConfigErrors(GraphicsConfig.Path, () => GraphicsConfig.Load(source).Validate());
        errors += ConfigErrors(GrassConfig.Path, () => GrassConfig.Load(source).Validate(db));
        errors += ConfigErrors(LightingConfig.Path, () => LightingConfig.Load(source).Validate());
        errors += ConfigErrors(UseStyleConfig.Path, () => UseStyleConfig.Load(source).Validate());
        errors += ConfigErrors(Strata.Core.Gameplay.Guide.BestiaryConfig.Path, () => Strata.Core.Gameplay.Guide.BestiaryConfig.Load(source).Validate());
        errors += ConfigErrors(CreatureMaterialsConfig.Path, () => CreatureMaterialsConfig.Load(source).Validate(db));
        errors += ConfigErrors(AmbienceConfig.Path, () => AmbienceConfig.Load(source).Validate(db));
        int warnings = db.Report.Warnings.Count();
        Console.WriteLine($"{errors} error(s), {warnings} warning(s)");
        return errors == 0 ? 0 : 1;
    }

    /// <summary>Prints a config file's problems (or why it does not parse) as errors and returns how many there were.</summary>
    private static int ConfigErrors(string path, Func<List<string>> validate)
    {
        List<string> problems;
        try { problems = validate(); }
        catch (System.Text.Json.JsonException e) { problems = new List<string> { e.Message }; }
        foreach (var problem in problems) Console.WriteLine($"Error: {path}: {problem}");
        return problems.Count;
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
