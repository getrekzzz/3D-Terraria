using Strata.Core.Content.Loading;

namespace Strata.Tools.Commands;

/// <summary>Regenerates data/manifest.json (or verifies it with --check).</summary>
public sealed class ManifestCommand : IToolCommand
{
    public string Name => "manifest";
    public string Description => "Regenerate data/manifest.json (--check to verify only)";

    public int Run(ToolContext context)
    {
        var model = ContentManifest.Build(context.PathOf("data"));
        var text = ContentManifest.Serialize(model);
        var path = context.PathOf(ContentLoader.ManifestPath);
        if (context.Options.Flag("check"))
        {
            var current = File.Exists(path) ? File.ReadAllText(path) : "";
            if (current != text)
            {
                Console.Error.WriteLine("data/manifest.json is out of date. Run: dotnet run --project tools/Strata.Tools -- manifest");
                return 1;
            }
            Console.WriteLine($"manifest OK ({model.Files.Count} files)");
            return 0;
        }
        File.WriteAllText(path, text);
        Console.WriteLine($"wrote {ContentLoader.ManifestPath} ({model.Files.Count} files)");
        return 0;
    }
}
