using System.Text.Json.Nodes;

namespace Strata.Core.Persistence.Migrations;

/// <summary>Upgrades one JSON save document from <see cref="FromVersion"/> to FromVersion + 1.</summary>
public interface IJsonMigration
{
    string Format { get; }
    int FromVersion { get; }
    void Apply(JsonObject document);
}

/// <summary>
/// Runs registered migrations in order until a document reaches the current version.
/// Every format change must add a migration here plus a fixture test.
/// </summary>
public sealed class SaveMigrations
{
    private readonly Dictionary<(string, int), IJsonMigration> _migrations = new();

    public static SaveMigrations Default { get; } = CreateDefault();

    public void Register(IJsonMigration migration) => _migrations[(migration.Format, migration.FromVersion)] = migration;

    /// <summary>Migrates in place. Returns the number of steps applied.</summary>
    public int Upgrade(JsonObject document, string format, int currentVersion)
    {
        int version = document["version"]?.GetValue<int>() ?? 0;
        if (version > currentVersion)
            throw new InvalidDataException($"{format} v{version} was written by a newer build (supports v{currentVersion}).");
        int steps = 0;
        while (version < currentVersion)
        {
            if (!_migrations.TryGetValue((format, version), out var m))
                throw new InvalidDataException($"No migration for {format} v{version} → v{version + 1}.");
            m.Apply(document);
            version++;
            document["version"] = version;
            steps++;
        }
        return steps;
    }

    private static SaveMigrations CreateDefault()
    {
        var m = new SaveMigrations();
        m.Register(new WorldMetaV0ToV1());
        return m;
    }
}

/// <summary>
/// v0 was the pre-release prototype layout (flat id lists, "seedText"). Kept as the reference example
/// of how migrations are written and tested.
/// </summary>
internal sealed class WorldMetaV0ToV1 : IJsonMigration
{
    public string Format => WorldMeta.FormatId;
    public int FromVersion => 0;

    public void Apply(JsonObject doc)
    {
        if (doc["seedText"] is JsonNode seedText && doc["seed"] == null)
        {
            doc["seed"] = long.TryParse(seedText.GetValue<string>(), out var s) ? s : 0L;
            doc.Remove("seedText");
        }
        if (doc["idTables"] == null)
        {
            doc["idTables"] = new JsonObject
            {
                ["blocks"] = doc["blockIds"]?.DeepClone() ?? new JsonArray(),
                ["items"] = doc["itemIds"]?.DeepClone() ?? new JsonArray(),
                ["tileObjects"] = new JsonArray(),
                ["prefixes"] = new JsonArray(),
            };
            doc.Remove("blockIds");
            doc.Remove("itemIds");
        }
        doc["format"] ??= WorldMeta.FormatId;
    }
}
