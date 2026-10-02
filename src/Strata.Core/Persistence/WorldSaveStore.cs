using System.Text.Json;
using System.Text.Json.Nodes;
using Strata.Core.Common;
using Strata.Core.Persistence.Migrations;

namespace Strata.Core.Persistence;

/// <summary>
/// On-disk layout of one world: <c>world.json</c> + <c>regions/r.X.Y.Z.srg</c>.
/// Thread-safe; callers may load chunks from worker threads. The in-memory region cache is authoritative for every
/// region touched this session: <see cref="ApplyChunks"/> publishes new copy-on-write region maps under a short lock,
/// and <see cref="WriteRegions"/> writes them to disk outside it, so loads never wait on a disk write.
/// </summary>
public sealed class WorldSaveStore
{
    public const string MetaFile = "world.json";
    public const string RegionDir = "regions";

    private readonly object _gate = new();
    private readonly object _writeGate = new();
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
    public void SaveChunks(IEnumerable<KeyValuePair<Int3, byte[]?>> chunks) => WriteRegions(ApplyChunks(chunks));

    /// <summary>
    /// Publishes chunk blobs (null = delete) to the region cache and returns the region images to write. Each touched
    /// region gets a fresh map (copy-on-write), so a snapshot handed to <see cref="WriteRegions"/> never changes.
    /// </summary>
    public IReadOnlyList<RegionWrite> ApplyChunks(IEnumerable<KeyValuePair<Int3, byte[]?>> chunks)
    {
        var updates = new Dictionary<Int3, Dictionary<int, byte[]>>();
        lock (_gate)
        {
            foreach (var (coord, blob) in chunks)
            {
                var region = RegionFile.RegionOf(coord);
                if (!updates.TryGetValue(region, out var map))
                {
                    map = new Dictionary<int, byte[]>(GetRegion(region));
                    updates[region] = map;
                    _regionCache[region] = map;
                }
                if (blob == null) map.Remove(RegionFile.LocalIndex(coord));
                else map[RegionFile.LocalIndex(coord)] = blob;
            }
        }
        var dir = Path.Combine(Directory, RegionDir);
        var writes = new List<RegionWrite>(updates.Count);
        foreach (var (region, map) in updates) writes.Add(new RegionWrite(Path.Combine(dir, RegionFile.FileName(region)), map));
        return writes;
    }

    /// <summary>Writes region images from <see cref="ApplyChunks"/> to disk, one writer at a time.</summary>
    public void WriteRegions(IReadOnlyList<RegionWrite> writes)
    {
        if (writes.Count == 0) return;
        lock (_writeGate)
        {
            System.IO.Directory.CreateDirectory(Path.Combine(Directory, RegionDir));
            foreach (var w in writes) RegionFile.Write(w.Path, w.Blobs);
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

/// <summary>A region file image to write: the full blob map at the moment it was published.</summary>
public sealed record RegionWrite(string Path, IReadOnlyDictionary<int, byte[]> Blobs);
