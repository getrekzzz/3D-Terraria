using System.Text.Json;
using System.Text.Json.Nodes;
using Strata.Core.Persistence;
using Strata.Core.Voxels;

namespace Strata.Core.Session;

public sealed partial class WorldSession
{
    /// <summary>
    /// Captures a modified chunk for the saver (main thread): its immutable voxel snapshot plus the encoded object
    /// section (objects are live state). Clears <see cref="Chunk.Modified"/>; the heavy encode runs on the save thread.
    /// </summary>
    public ChunkSnapshot Capture(Chunk chunk)
    {
        var objects = ChunkCodec.EncodeObjects(World.TileObjects.InChunk(chunk.Coord), Ids);
        var snapshot = new ChunkSnapshot(chunk.Coord, chunk.Capture(), chunk.MutationLevel, objects, Saver?.NextSeq() ?? 0);
        chunk.Modified = false;
        return snapshot;
    }

    /// <summary>A save batch of every modified loaded chunk plus world.json, captured on the main thread.</summary>
    public SaveBatch BeginSave()
    {
        var batch = new SaveBatch();
        foreach (var chunk in World.Chunks)
            if (chunk.Modified) batch.Chunks.Add(Capture(chunk));
        RefreshMeta();
        batch.Meta = SerializeMeta();
        return batch;
    }

    /// <summary>Copies live world state (time, flags, counters, wires, mutations, id tables) into <see cref="Meta"/>.</summary>
    private void RefreshMeta()
    {
        Meta.Version = WorldMeta.CurrentVersion;
        Meta.WorldTick = Clock.Tick;
        Meta.LastPlayedUtc = DateTime.UtcNow;
        Meta.Flags = Flags.OrderBy(f => f, StringComparer.Ordinal).ToList();
        Meta.Counters = new Dictionary<string, int>(Counters, StringComparer.Ordinal);
        Meta.Wires = Wires.OrderBy(c => c.Y).ThenBy(c => c.Z).ThenBy(c => c.X).SelectMany(c => new[] { c.X, c.Y, c.Z }).ToList();
        Mutations.Persist(Meta);
        Meta.StoreIdTables(Ids);
    }

    private JsonObject SerializeMeta() => (JsonObject)JsonSerializer.SerializeToNode(Meta, SaveJson.Options)!;

    /// <summary>Waits for pending writes and stops the save thread.</summary>
    public void Dispose() => Saver?.Dispose();
}
