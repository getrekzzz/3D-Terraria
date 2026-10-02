using Strata.Core.Common;
using Strata.Core.Diagnostics;
using Strata.Core.Meshing;
using Strata.Core.Persistence;
using Strata.Core.Session;
using Strata.Core.Tests.Support;
using Strata.Core.World;

namespace Strata.Core.Tests.Persistence;

public class WorldSaverTests
{
    private static readonly RenderTables Tables = new(TestPaths.Content, _ => 0);

    private static WorldSession NewWorld(string dir, MemoryLog log) =>
        WorldSession.Create(TestPaths.Content, new WorldSettings { Name = "saver", Seed = 7, Size = WorldSizePreset.Tiny }, dir, Tables, log);

    private static Int3 SpawnChunk(WorldSession s) => ChunkMath.CellToChunk(s.SpawnCell);

    private static void Place(WorldSession s, Int3 cell, string block)
    {
        var batch = s.World.BeginEdit();
        batch.SetBlock(cell, s.Content.BlockId(block));
        s.Commit(batch);
    }

    private static ushort ReadBack(string dir, Int3 cell, MemoryLog log)
    {
        var reloaded = WorldSession.Load(TestPaths.Content, dir, Tables, log);
        var product = reloaded.Produce(ChunkMath.CellToChunk(cell));
        Assert.True(product.FromSave);
        reloaded.Install(product);
        return reloaded.World.GetBlock(cell);
    }

    [Fact]
    public void EditsSurviveReloadWhileWriteIsPending()
    {
        var dir = TestWorlds.TempDir();
        var log = new MemoryLog();
        var s = NewWorld(dir, log);
        var coord = SpawnChunk(s);
        s.Install(s.Produce(coord));
        var cell = ChunkMath.ChunkOrigin(coord) + new Int3(5, 6, 7);
        Place(s, cell, "gray_brick");
        var brick = s.Content.BlockId("gray_brick");

        using var gate = new ManualResetEventSlim(false);
        s.Saver!.TestGate = gate;
        var snapshot = s.Unload(coord);
        Assert.NotNull(snapshot);
        s.SaveUnloaded(new[] { snapshot! }, wait: false);
        Assert.False(s.World.HasChunk(coord));

        // Streamed back in before the write lands: the product comes from the pending snapshot.
        var product = s.Produce(coord);
        Assert.True(product.FromSave);
        s.Install(product);
        Assert.Equal(brick, s.World.GetBlock(cell));

        gate.Set();
        Assert.True(s.Saver.Flush(TimeSpan.FromSeconds(30)));
        Assert.Equal(0, s.Saver.PendingCount);
        Assert.Equal(brick, ReadBack(dir, cell, log));
        Assert.Equal(0, log.Count(LogLevel.Error));
    }

    [Fact]
    public void LoadChunkDoesNotWaitForRegionWrite()
    {
        var dir = TestWorlds.TempDir();
        var log = new MemoryLog();
        var s = NewWorld(dir, log);
        var a = SpawnChunk(s);
        var b = new Int3(a.X ^ 1, a.Y, a.Z); // same region file
        Assert.Equal(RegionFile.RegionOf(a), RegionFile.RegionOf(b));
        s.Install(s.Produce(a));
        s.Install(s.Produce(b));
        Place(s, ChunkMath.ChunkOrigin(a) + new Int3(1, 1, 1), "gray_brick");
        Place(s, ChunkMath.ChunkOrigin(b) + new Int3(1, 1, 1), "gray_brick");

        using var writing = new ManualResetEventSlim(false);
        s.Saver!.BeforeWrite = () =>
        {
            writing.Set();
            Thread.Sleep(500);
        };
        s.SaveUnloaded(new[] { s.Unload(a)!, s.Unload(b)! }, wait: false);
        Assert.True(writing.Wait(TimeSpan.FromSeconds(30)));

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var blob = s.Store!.LoadChunk(b);
        sw.Stop();
        Assert.NotNull(blob);
        Assert.True(sw.ElapsedMilliseconds < 200, $"LoadChunk waited {sw.ElapsedMilliseconds} ms for the region write");
        Assert.True(s.Saver.Flush(TimeSpan.FromSeconds(30)));
        Assert.Equal(0, log.Count(LogLevel.Error));
    }

    [Fact]
    public void NewBlockIdsReachWorldJson()
    {
        var dir = TestWorlds.TempDir();
        var log = new MemoryLog();
        var s = NewWorld(dir, log);
        Assert.DoesNotContain("gray_brick", s.Ids.Blocks.Names);
        var coord = SpawnChunk(s);
        s.Install(s.Produce(coord));
        var cell = ChunkMath.ChunkOrigin(coord) + new Int3(3, 3, 3);
        Place(s, cell, "gray_brick");

        // Unload-only batch: no world.json in the batch, but encoding interns gray_brick. world.json is written before
        // the region file, so a crash in between never leaves a blob with an id the tables lack.
        bool tablesFirst = false;
        s.Saver!.BeforeWrite = () => tablesFirst = new WorldSaveStore(dir).LoadMeta().IdTables.Blocks.Contains("gray_brick");
        s.SaveUnloaded(new[] { s.Unload(coord)! }, wait: false);
        Assert.True(s.Saver.Flush(TimeSpan.FromSeconds(30)));
        Assert.True(tablesFirst);

        Assert.Contains("gray_brick", new WorldSaveStore(dir).LoadMeta().IdTables.Blocks);
        var block = ReadBack(dir, cell, log);
        Assert.Equal(s.Content.BlockId("gray_brick"), block);
        Assert.Equal(0, log.Count(LogLevel.Error));
    }

    [Fact]
    public void LatestSnapshotWins()
    {
        var dir = TestWorlds.TempDir();
        var log = new MemoryLog();
        var s = NewWorld(dir, log);
        var coord = SpawnChunk(s);
        s.Install(s.Produce(coord));
        var cell = ChunkMath.ChunkOrigin(coord) + new Int3(2, 2, 2);
        Place(s, cell, "gray_brick");
        var older = s.Capture(s.World.GetChunk(coord)!);
        Place(s, cell, "wood");
        var newer = s.Capture(s.World.GetChunk(coord)!);
        Assert.True(newer.Seq > older.Seq);

        // The newer capture is written first; the older one arrives later (and also shares a batch) and must lose.
        var first = new SaveBatch();
        first.Chunks.Add(newer);
        s.Saver!.Enqueue(first);
        Assert.True(s.Saver.Flush(TimeSpan.FromSeconds(30)));
        var second = new SaveBatch();
        second.Chunks.Add(older);
        second.Chunks.Add(newer);
        second.Chunks.Add(older);
        s.Saver.Enqueue(second);
        Assert.True(s.Saver.Flush(TimeSpan.FromSeconds(30)));

        Assert.Equal(s.Content.BlockId("wood"), ReadBack(dir, cell, log));
        Assert.Equal(0, log.Count(LogLevel.Error));
    }
}
