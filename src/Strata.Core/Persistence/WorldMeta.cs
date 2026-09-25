using System.Text.Json;
using System.Text.Json.Serialization;
using Strata.Core.World;

namespace Strata.Core.Persistence;

/// <summary>
/// world.json — everything about a world except chunk data. Unknown fields are preserved
/// (<see cref="Extra"/>) so an older build never deletes data written by a newer one.
/// </summary>
public sealed class WorldMeta
{
    public const string FormatId = "strata-world";
    public const int CurrentVersion = 1;

    public string Format { get; set; } = FormatId;
    public int Version { get; set; } = CurrentVersion;

    public string Name { get; set; } = "";
    public long Seed { get; set; }
    public WorldSizePreset Size { get; set; }
    public Difficulty Difficulty { get; set; }
    public EvilKind Evil { get; set; }

    /// <summary>World generator version that produced unmodified regions.</summary>
    public int GeneratorVersion { get; set; }

    /// <summary>Hash of the world plan, to detect generator drift.</summary>
    public string PlanHash { get; set; } = "";

    public DateTime CreatedUtc { get; set; }
    public DateTime LastPlayedUtc { get; set; }
    public long PlayTicks { get; set; }

    /// <summary>Absolute world clock in ticks (time of day, moon phase derive from it).</summary>
    public long WorldTick { get; set; }

    public int MutationLevel { get; set; }
    public List<string> Flags { get; set; } = new();
    public List<string> ActiveEvents { get; set; } = new();
    public int[] Spawn { get; set; } = { 0, 0, 0 };

    public IdTablesModel IdTables { get; set; } = new();

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }

    public sealed class IdTablesModel
    {
        public List<string> Blocks { get; set; } = new();
        public List<string> Items { get; set; } = new();
        public List<string> TileObjects { get; set; } = new();
        public List<string> Prefixes { get; set; } = new();

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? Extra { get; set; }
    }

    public WorldSettings ToSettings() => new() { Name = Name, Seed = Seed, Size = Size, Difficulty = Difficulty, Evil = Evil };

    public WorldIdTables CreateIdTables() => new()
    {
        Blocks = new SaveIdTable(IdTables.Blocks),
        Items = new SaveIdTable(IdTables.Items),
        TileObjects = new SaveIdTable(IdTables.TileObjects),
        Prefixes = new SaveIdTable(IdTables.Prefixes),
    };

    public void StoreIdTables(WorldIdTables tables)
    {
        IdTables.Blocks = tables.Blocks.Names.ToList();
        IdTables.Items = tables.Items.Names.ToList();
        IdTables.TileObjects = tables.TileObjects.Names.ToList();
        IdTables.Prefixes = tables.Prefixes.Names.ToList();
    }
}
