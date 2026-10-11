using Strata.Core.Common;
using Strata.Core.Persistence;
using Strata.Core.Voxels;

namespace Strata.Core.Session;

public sealed partial class WorldSession
{
    /// <summary>Applies an edit batch and updates the sky map; returns every chunk that needs remeshing.</summary>
    public HashSet<Int3> Commit(EditBatch batch)
    {
        var result = batch.Commit();
        var dirty = new HashSet<Int3>(result.AffectedChunks);
        foreach (var column in result.ChangedChunks.Select(c => (c.X, c.Z)).Distinct())
            foreach (var change in Sky.Rebuild(column.X, column.Z))
                AddSkyInfluence(dirty, change);
        return dirty;
    }

    /// <summary>Chunks whose lighting depends on a column whose sky exposure changed.</summary>
    public void AddSkyInfluence(HashSet<Int3> dirty, (int X, int Z, int Old, int New) change)
    {
        const int r = Meshing.Neighborhood.Margin;
        int y0 = Math.Min(change.Old, change.New) - r, y1 = Math.Max(change.Old, change.New) + r;
        var min = ChunkMath.CellToChunk(new Int3(change.X - r, y0, change.Z - r));
        var max = ChunkMath.CellToChunk(new Int3(change.X + r, y1, change.Z + r));
        for (int cy = min.Y; cy <= max.Y; cy++)
            for (int cz = min.Z; cz <= max.Z; cz++)
                for (int cx = min.X; cx <= max.X; cx++)
                {
                    var c = new Int3(cx, cy, cz);
                    if (World.HasChunk(c)) dirty.Add(c);
                }
    }

    /// <summary>Writes world.json (time, flags, id tables) without touching chunks. Waits for the write.</summary>
    public void SaveMetaOnly()
    {
        if (Saver == null) return;
        RefreshMeta();
        Saver.Enqueue(new SaveBatch { Meta = SerializeMeta() });
        Saver.Flush();
    }

    /// <summary>Saves every modified loaded chunk plus metadata and waits for the writes. Returns the number of chunks saved.</summary>
    public int SaveAll()
    {
        if (Saver == null) return 0;
        var batch = BeginSave();
        Saver.Enqueue(batch);
        Saver.Flush();
        return batch.Chunks.Count;
    }

    /// <summary>
    /// Queues snapshots returned by <see cref="Unload"/>. Until written they stay readable by <see cref="Produce"/>;
    /// world.json is rewritten after them if encoding added ids.
    /// </summary>
    public void SaveUnloaded(IReadOnlyList<ChunkSnapshot> snapshots, bool wait)
    {
        if (Saver == null || snapshots.Count == 0) return;
        var batch = new SaveBatch();
        batch.Chunks.AddRange(snapshots);
        Saver.Enqueue(batch);
        if (wait) Saver.Flush();
    }

    /// <summary>Finds the first standable cell at or above the plan spawn (ground below, headroom above).</summary>
    public Int3 FindStandableNear(Int3 cell, int headroom = 4)
    {
        for (int dy = 0; dy < 64; dy++)
        {
            foreach (int sign in new[] { 1, -1 })
            {
                int y = cell.Y + dy * sign;
                if (IsStandable(cell.X, y, cell.Z, headroom)) return new Int3(cell.X, y, cell.Z);
            }
        }
        return cell;
    }

    public bool IsStandable(int x, int y, int z, int headroom)
    {
        if (!World.IsSolid(x, y - 1, z) || World.GetBlock(x, y - 1, z) == VoxelWorld.Unknown) return false;
        for (int h = 0; h < headroom; h++)
        {
            ushort b = World.GetBlock(x, y + h, z);
            if (b == VoxelWorld.Unknown || Content.Blocks[b].IsSolid || World.GetLiquid(x, y + h, z) != 0) return false;
        }
        return true;
    }
}
