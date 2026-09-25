using System.Text.Json;
using Strata.Core.Content.Defs;
using Strata.Core.Content.Validation;

namespace Strata.Core.Content.Loading;

/// <summary>
/// Loads content files listed in <c>data/manifest.json</c>. Every file is a JSON object whose keys
/// are category names ("blocks", "items", …) mapping to arrays of definitions, so a file may hold a
/// whole feature pack (e.g. a biome's blocks, items, enemies and recipes together).
/// </summary>
public static class ContentLoader
{
    public const string ManifestPath = "data/manifest.json";
    public const string DataRoot = "data/";

    private delegate void CategoryReader(ContentDatabase db, JsonElement array, string file);

    private static readonly Dictionary<string, CategoryReader> Categories = new(StringComparer.Ordinal)
    {
        ["blocks"] = (db, a, f) => ReadInto(db, db.Blocks, a, f),
        ["items"] = (db, a, f) => ReadInto(db, db.Items, a, f),
        ["tileObjects"] = (db, a, f) => ReadInto(db, db.TileObjects, a, f),
        ["recipes"] = (db, a, f) => ReadInto(db, db.Recipes, a, f),
        ["recipeGroups"] = (db, a, f) => ReadInto(db, db.RecipeGroups, a, f),
        ["biomes"] = (db, a, f) => ReadInto(db, db.Biomes, a, f),
        ["enemies"] = (db, a, f) => ReadInto(db, db.Enemies, a, f),
        ["spawnRules"] = (db, a, f) => ReadInto(db, db.SpawnRules, a, f),
        ["projectiles"] = (db, a, f) => ReadInto(db, db.Projectiles, a, f),
        ["buffs"] = (db, a, f) => ReadInto(db, db.Buffs, a, f),
        ["prefixes"] = (db, a, f) => ReadInto(db, db.Prefixes, a, f),
        ["lootTables"] = (db, a, f) => ReadInto(db, db.LootTables, a, f),
        ["flags"] = (db, a, f) => ReadInto(db, db.Flags, a, f),
        ["oreVeins"] = (db, a, f) => ReadInto(db, db.OreVeins, a, f),
        ["events"] = (db, a, f) => ReadInto(db, db.Events, a, f),
        ["liquids"] = (db, a, f) => ReadInto(db, db.Liquids, a, f),
    };

    public static IReadOnlyCollection<string> CategoryNames => Categories.Keys;

    /// <summary>Loads and validates all content. Never throws for data errors; inspect the report.</summary>
    public static ContentDatabase Load(IDataSource source, ContentValidationOptions? validation = null)
    {
        var db = new ContentDatabase();
        IReadOnlyList<string> files;
        try
        {
            files = ContentManifest.Read(source);
        }
        catch (Exception ex) when (ex is JsonException or IOException or InvalidDataException)
        {
            db.Report.Error(ManifestPath, "manifest", ex.Message);
            db.Finish();
            return db;
        }

        foreach (var file in files) LoadFile(db, source, DataRoot + file);
        DerivedContent.Apply(db);
        db.Finish();
        ContentValidator.Validate(db, validation ?? new ContentValidationOptions());
        return db;
    }

    /// <summary>Loads a single JSON text into a database (used by tests).</summary>
    public static ContentDatabase LoadFromTexts(IEnumerable<(string Name, string Json)> files, ContentValidationOptions? validation = null)
    {
        var db = new ContentDatabase();
        foreach (var (name, json) in files) LoadJson(db, json, name);
        DerivedContent.Apply(db);
        db.Finish();
        ContentValidator.Validate(db, validation ?? new ContentValidationOptions());
        return db;
    }

    private static void LoadFile(ContentDatabase db, IDataSource source, string path)
    {
        if (!source.Exists(path))
        {
            db.Report.Error(path, "file", "Listed in manifest but missing.");
            return;
        }
        LoadJson(db, source.ReadText(path), path);
    }

    private static void LoadJson(ContentDatabase db, string json, string file)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        }
        catch (JsonException ex)
        {
            db.Report.Error(file, "json", ex.Message);
            return;
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                db.Report.Error(file, "json", "Root must be an object of category arrays.");
                return;
            }
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (!Categories.TryGetValue(prop.Name, out var reader))
                {
                    db.Report.Error(file, prop.Name, $"Unknown content category. Known: {string.Join(", ", Categories.Keys)}.");
                    continue;
                }
                if (prop.Value.ValueKind != JsonValueKind.Array)
                {
                    db.Report.Error(file, prop.Name, "Category value must be an array.");
                    continue;
                }
                reader(db, prop.Value, file);
            }
        }
    }

    private static void ReadInto<T>(ContentDatabase db, Registry<T> registry, JsonElement array, string file) where T : ContentDef
    {
        int index = 0;
        foreach (var element in array.EnumerateArray())
        {
            string subject = $"{registry.Category}[{index}]";
            if (element.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.String)
                subject = $"{registry.Category}:{idProp.GetString()}";
            try
            {
                var def = element.Deserialize<T>(ContentJson.Options);
                if (def == null)
                {
                    db.Report.Error(file, subject, "Null definition.");
                }
                else
                {
                    def.SourceFile = file;
                    if (!registry.TryAdd(def)) db.Report.Error(file, subject, $"Duplicate {registry.Category} id.");
                }
            }
            catch (JsonException ex)
            {
                db.Report.Error(file, subject, ex.Message);
            }
            index++;
        }
    }
}
