using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using Strata.Core.Common;
using Strata.Core.Content;
using Strata.Core.Diagnostics;
using Strata.Core.Voxels;

namespace Strata.Core.Persistence;

/// <summary>
/// A chunk captured on the main thread for saving: immutable voxel data plus its already-encoded object section.
/// <see cref="Seq"/> orders captures of the same chunk (the newest one always wins).
/// </summary>
public sealed record ChunkSnapshot(Int3 Coord, ChunkData Data, int MutationLevel, byte[] Objects, long Seq);

/// <summary>
/// A file written by the saver. <see cref="Bytes"/> runs on the save thread and may return null to skip the write
/// (the content became obsolete); <see cref="Failed"/> runs on the main thread (through the poster) if the write fails.
/// </summary>
public sealed record SaveFile(string Path, Func<byte[]?> Bytes, Action? Failed = null);

/// <summary>One unit of save work: chunk snapshots, an optional world.json node and extra files (player, maps).</summary>
public sealed class SaveBatch
{
    public List<ChunkSnapshot> Chunks { get; } = new();

    /// <summary>world.json as serialized on the main thread; its id tables are refreshed after the chunks are encoded.</summary>
    public JsonObject? Meta { get; set; }

    /// <summary>Files written after the world (bytes are produced on the save thread).</summary>
    public List<SaveFile> Files { get; } = new();

    /// <summary>Files deleted after the writes (ordered after every earlier batch, so a queued write cannot resurrect them).</summary>
    public List<string> Deletes { get; } = new();

    /// <summary>Runs on the main thread (through the poster) with the number of chunks written.</summary>
    public Action<int>? Done { get; set; }
}

/// <summary>
/// The single writer of a world's files. A background thread ("strata-saver") encodes chunk snapshots, publishes them
/// to the region cache, writes region files, then world.json (after the blobs, so its id tables cover every id they
/// use), then extra files. Snapshots stay readable through <see cref="TryGetPending"/> until their blob is in the
/// cache, so a chunk streamed back in before its write lands keeps its edits. The thread exits when idle and restarts
/// on the next batch.
/// </summary>
public sealed class WorldSaver : IDisposable
{
    private const int IdleExitMs = 3000;

    private readonly WorldSaveStore _store;
    private readonly ContentDatabase _db;
    private readonly WorldIdTables _ids;
    private readonly ILog _log;
    private readonly ConcurrentDictionary<Int3, ChunkSnapshot> _pending = new();
    private readonly Queue<SaveBatch> _queue = new();
    private readonly object _gate = new();
    private readonly List<ChunkSnapshot> _retry = new(); // worker only
    private readonly Dictionary<Int3, long> _written = new(); // worker only: newest Seq on disk per chunk
    private JsonObject? _lastMeta; // worker only after construction
    private int _lastIdCount = -1;
    private Thread? _thread;
    private bool _busy, _disposed;
    private long _seq;

    public WorldSaver(WorldSaveStore store, ContentDatabase db, WorldIdTables ids, ILog log)
    {
        _store = store;
        _db = db;
        _ids = ids;
        _log = log;
    }

    /// <summary>Delivers <see cref="SaveBatch.Done"/> callbacks to the main thread; null runs them on the save thread.</summary>
    public Action<Action>? Post { get; set; }

    /// <summary>True while a batch is queued or being written.</summary>
    public bool InFlight
    {
        get { lock (_gate) return _busy || _queue.Count > 0; }
    }

    public int PendingCount => _pending.Count;

    /// <summary>Test hook: the worker waits on this before each batch (hold it to keep writes pending).</summary>
    internal ManualResetEventSlim? TestGate { get; set; }

    /// <summary>Test hook: runs on the worker right before region files are written.</summary>
    internal Action? BeforeWrite { get; set; }

    public long NextSeq() => Interlocked.Increment(ref _seq);

    /// <summary>Seeds the world.json used when an unload-only batch must rewrite the id tables (main thread, at load).</summary>
    public void SetBaseMeta(JsonObject meta)
    {
        lock (_gate) _lastMeta ??= meta;
    }

    /// <summary>The newest not-yet-published snapshot of a chunk, if one is waiting. Thread-safe.</summary>
    public bool TryGetPending(Int3 coord, out ChunkSnapshot snapshot) => _pending.TryGetValue(coord, out snapshot!);

    /// <summary>Queues a batch (main thread). Its snapshots are readable through <see cref="TryGetPending"/> at once.</summary>
    public void Enqueue(SaveBatch batch)
    {
        foreach (var s in batch.Chunks)
            _pending.AddOrUpdate(s.Coord, s, (_, old) => old.Seq > s.Seq ? old : s);
        lock (_gate)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(WorldSaver));
            _queue.Enqueue(batch);
            if (_thread == null)
            {
                _thread = new Thread(Run) { IsBackground = true, Name = "strata-saver", Priority = ThreadPriority.BelowNormal };
                _thread.Start();
            }
            else
            {
                Monitor.PulseAll(_gate);
            }
        }
    }

    /// <summary>Blocks until every queued batch is written (<see cref="Timeout.InfiniteTimeSpan"/> waits forever). Returns false on timeout.</summary>
    public bool Flush(TimeSpan timeout)
    {
        bool forever = timeout == Timeout.InfiniteTimeSpan;
        var deadline = forever ? DateTime.MaxValue : DateTime.UtcNow + timeout;
        lock (_gate)
        {
            while (_busy || _queue.Count > 0)
            {
                if (forever)
                {
                    Monitor.Wait(_gate);
                    continue;
                }
                var left = deadline - DateTime.UtcNow;
                if (left <= TimeSpan.Zero) return false;
                Monitor.Wait(_gate, left);
            }
        }
        return true;
    }

    /// <summary>Flushes with a generous bound, logging a warning if the writes did not finish in time.</summary>
    public bool Flush()
    {
        if (Flush(TimeSpan.FromMinutes(5))) return true;
        _log.Warn("save", "Saving is taking longer than 5 minutes; continuing while the save thread keeps writing.");
        return false;
    }

    private void Run()
    {
        while (true)
        {
            SaveBatch batch;
            lock (_gate)
            {
                while (_queue.Count == 0)
                {
                    _busy = false;
                    Monitor.PulseAll(_gate);
                    if (_disposed || !Monitor.Wait(_gate, IdleExitMs) && _queue.Count == 0)
                    {
                        _thread = null;
                        return;
                    }
                }
                batch = _queue.Dequeue();
                _busy = true;
            }
            TestGate?.Wait();
            int written = 0;
            try
            {
                written = Process(batch);
            }
            catch (Exception ex)
            {
                _log.Error("save", $"Save batch failed ({batch.Chunks.Count} chunks, meta={batch.Meta != null}, files={batch.Files.Count}); its chunks are retried with the next save", ex);
                _retry.AddRange(batch.Chunks);
            }
            if (batch.Done is { } done)
            {
                int n = written;
                if (Post is { } post) post(() => done(n));
                else done(n);
            }
        }
    }

    private int Process(SaveBatch batch)
    {
        // Newest capture per chunk, including earlier failures; anything superseded by a later capture is skipped.
        var chosen = new Dictionary<Int3, ChunkSnapshot>();
        foreach (var s in _retry.Concat(batch.Chunks))
        {
            if (_pending.TryGetValue(s.Coord, out var newest) && newest.Seq > s.Seq) continue;
            if (_written.TryGetValue(s.Coord, out var onDisk) && onDisk >= s.Seq)
            {
                _pending.TryRemove(new KeyValuePair<Int3, ChunkSnapshot>(s.Coord, s)); // already in the cache
                continue;
            }
            if (!chosen.TryGetValue(s.Coord, out var have) || have.Seq < s.Seq) chosen[s.Coord] = s;
        }
        _retry.Clear();

        var blobs = new List<KeyValuePair<Int3, byte[]?>>(chosen.Count);
        foreach (var s in chosen.Values)
        {
            try
            {
                blobs.Add(new(s.Coord, ChunkCodec.Encode(s.Data, s.MutationLevel, s.Objects, _db, _ids)));
            }
            catch (Exception ex)
            {
                _log.Error("save", $"Encoding chunk {s.Coord} failed; it stays pending", ex);
                _retry.Add(s);
            }
        }

        IReadOnlyList<RegionWrite> writes = Array.Empty<RegionWrite>();
        if (blobs.Count > 0)
        {
            try
            {
                writes = _store.ApplyChunks(blobs);
            }
            catch (Exception ex)
            {
                _log.Error("save", $"Publishing {blobs.Count} chunks failed; they stay pending and are retried with the next save", ex);
                _retry.AddRange(blobs.Select(b => chosen[b.Key]));
                blobs.Clear();
            }
        }

        // world.json first: its id tables (which only grow) then cover every id the region blobs use, even after a crash.
        WriteMeta(batch.Meta);

        if (blobs.Count > 0)
        {
            foreach (var (coord, _) in blobs)
            {
                var snap = chosen[coord];
                _written[coord] = snap.Seq;
                _pending.TryRemove(new KeyValuePair<Int3, ChunkSnapshot>(coord, snap));
            }
            BeforeWrite?.Invoke();
            try
            {
                _store.WriteRegions(writes);
            }
            catch (Exception ex)
            {
                // The cache already holds the blobs (loads stay correct); the next batch rewrites these regions.
                _log.Error("save", $"Writing {writes.Count} region files failed; retrying with the next save", ex);
                foreach (var (coord, _) in blobs) _written.Remove(coord);
                _retry.AddRange(blobs.Select(b => chosen[b.Key]));
            }
        }

        foreach (var file in batch.Files)
        {
            try
            {
                if (file.Bytes() is { } bytes) AtomicFile.WriteAllBytes(file.Path, bytes);
            }
            catch (Exception ex)
            {
                _log.Error("save", $"Writing {file.Path} failed", ex);
                if (file.Failed is { } failed)
                {
                    if (Post is { } post) post(failed);
                    else failed();
                }
            }
        }
        foreach (var path in batch.Deletes)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception ex)
            {
                _log.Error("save", $"Deleting {path} failed", ex);
            }
        }
        return blobs.Count;
    }

    /// <summary>Writes world.json with the current id tables (also when only the tables grew).</summary>
    private void WriteMeta(JsonObject? meta)
    {
        int idCount = _ids.Blocks.Count + _ids.Items.Count + _ids.TileObjects.Count + _ids.Prefixes.Count;
        lock (_gate)
        {
            if (meta != null) _lastMeta = meta;
            else if (idCount == _lastIdCount || _lastMeta == null) return;
            meta = _lastMeta;
        }
        meta["idTables"] = JsonSerializer.SerializeToNode(new WorldMeta.IdTablesModel
        {
            Blocks = _ids.Blocks.Names.ToList(),
            Items = _ids.Items.Names.ToList(),
            TileObjects = _ids.TileObjects.Names.ToList(),
            Prefixes = _ids.Prefixes.Names.ToList(),
        }, SaveJson.Options);
        try
        {
            AtomicFile.WriteAllText(Path.Combine(_store.Directory, WorldSaveStore.MetaFile), meta.ToJsonString(SaveJson.Options));
            _lastIdCount = idCount;
        }
        catch (Exception ex)
        {
            _log.Error("save", "Writing world.json failed", ex);
        }
    }

    /// <summary>Waits for every queued write (however long it takes: quitting must not lose data), then stops the thread.</summary>
    public void Dispose()
    {
        if (!Flush(TimeSpan.FromSeconds(30)))
        {
            _log.Warn("save", "Still writing the world after 30 s; waiting for the save thread to finish.");
            Flush(Timeout.InfiniteTimeSpan);
        }
        Thread? thread;
        lock (_gate)
        {
            _disposed = true;
            thread = _thread;
            Monitor.PulseAll(_gate);
        }
        thread?.Join();
    }
}
