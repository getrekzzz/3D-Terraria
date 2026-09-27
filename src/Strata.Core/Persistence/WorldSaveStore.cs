using System.Text.Json;
using System.Text.Json.Nodes;
using Strata.Core.Common;
using Strata.Core.Persistence.Migrations;

namespace Strata.Core.Persistence;

/// <summary>
/// On-disk layout of one world: <c>world.json</c> + <c>regions/r.X.Y.Z.srg</c>.
/// Thread-safe (region IO is serialized); callers may load chunks from worker threads.
/// </summary>
public sealed class WorldSaveStore
{
    public const string MetaFile = "world.json";
    public const string RegionDir = "regions";

    private readonly object _gate = new();
    private readonly Dictionary<Int3, Dictionary<int, byte[]>> _regionCache = new();

    public WorldSaveStore(string directory)
    {
        Directory = Path.GetFullPath(directory);
    }

    public string Directory { get; }

    public bool Exists => File.Exists(Path.Combine(Directory, MetaFile));

    public WorldMeta LoadMeta()
    {
        var path = Path.Combine(Directory, MetaFile);
        var bytes = AtomicFile.ReadWithFallback(path) ?? throw new FileNotFoundException("World metadata missing.", path);
        var node = JsonNode.Parse(bytes) as JsonObject ?? throw new InvalidDataException("world.json is not an object.");
        SaveMigrations.Default.Upgrade(node, WorldMeta.FormatId, WorldMeta.CurrentVersion);
        var meta = node.Deserialize<WorldMeta>(SaveJson.Options) ?? throw new InvalidDataException("world.json is empty.");
        if (meta.Format != WorldMeta.FormatId) throw new InvalidDataException($"Unexpected format '{meta.Format}'.");
        return meta;
    }

    public void SaveMeta(WorldMeta meta)
    {
        meta.Version = WorldMeta.CurrentVersion;
        AtomicFile.WriteAllText(Path.Combine(Directory, MetaFile), JsonSerializer.Serialize(meta, SaveJson.Options));
    }

    /// <summary>The stored blob for a chunk, or null if the chunk was never modified.</summary>
    public byte[]? LoadChunk(Int3 chunk)
    {
        lock (_gate)
        {
            var region = GetRegion(RegionFile.RegionOf(chunk));
            return region.TryGetValue(RegionFile.LocalIndex(chunk), out var blob) ? blob : null;
        }
    }

    /// <summary>Writes chunk blobs (null = delete) grouped per region file.</summary>
    public void SaveChunks(IEnumerable<KeyValuePair<Int3, byte[]?>> chunks)
    {
        lock (_gate)
        {
            foreach (var group in chunks.GroupBy(kv => RegionFile.RegionOf(kv.Key)))
            {
                var updates = group.ToDictionary(kv => RegionFile.LocalIndex(kv.Key), kv => kv.Value);
                var dir = Path.Combine(Directory, RegionDir);
                System.IO.Directory.CreateDirectory(dir);
                RegionFile.Update(Path.Combine(dir, RegionFile.FileName(group.Key)), updates);
                _regionCache.Remove(group.Key);
            }
        }
    }

    private Dictionary<int, byte[]> GetRegion(Int3 region)
    {
        if (!_regionCache.TryGetValue(region, out var blobs))
        {
            blobs = RegionFile.ReadAll(Path.Combine(Directory, RegionDir, RegionFile.FileName(region)));
            _regionCache[region] = blobs;
        }
        return blobs;
    }
}
