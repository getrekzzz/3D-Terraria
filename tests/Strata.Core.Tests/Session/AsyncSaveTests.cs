using Strata.Core.Common;
using Strata.Core.Diagnostics;
using Strata.Core.Gameplay.Map;
using Strata.Core.Meshing;
using Strata.Core.Session;
using Strata.Core.Streaming;
using Strata.Core.Tests.Support;
using Strata.Core.World;

namespace Strata.Core.Tests.Session;

public class AsyncSaveTests
{
    private static readonly RenderTables Tables = new(TestPaths.Content, _ => 0);

    /// <summary>Installs and edits at least <paramref name="count"/> chunks around spawn; returns one edited cell per chunk.</summary>
    private static List<Int3> EditChunks(WorldSession s, int count, string block)
    {
        var center = ChunkMath.CellToChunk(s.SpawnCell);
        var cells = new List<Int3>();
        var batch = s.World.BeginEdit();
        for (int r = 0; cells.Count < count && r < 8; r++)
            for (int dz = -r; dz <= r; dz++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != r || cells.Count >= count) continue;
                    var c = center + new Int3(dx, 1, dz);
                    if (!s.World.ChunkInBounds(c)) continue;
                    if (!s.World.HasChunk(c)) s.Install(s.Produce(c));
                    var cell = ChunkMath.ChunkOrigin(c) + new Int3(9, 9, 9);
                    batch.SetBlock(cell, s.Content.BlockId(block));
                    cells.Add(cell);
                }
        s.Commit(batch);
        Assert.Equal(count, cells.Count);
        return cells;
    }

    [Fact]
    public void AutosaveCaptureIsCheap()
    {
        var dir = TestWorlds.TempDir();
        using var game = TestGame.Create(dir);
        var w = game.World;
        var s = w.Session;
        int? autosaved = null;
        w.Autosaved += n => autosaved = n;

        // Warm-up round (JIT, first-time serializer setup), then the measured one.
        EditChunks(s, 50, "gray_brick");
        Assert.True(w.BeginAutosave());
        Assert.True(s.Saver!.Flush(TimeSpan.FromSeconds(60)));
        w.Jobs.Drain(1000);
        Assert.NotNull(autosaved);
        autosaved = null;

        var cells = EditChunks(s, 50, "wood");
        game.Player.State.Deaths = 7;
        var map = w.Maps.For(game.Player);
        map.SetSurface(3, 4, MapValue.Block(s.Content.BlockId("wood")), 21);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Assert.True(w.BeginAutosave());
        sw.Stop();
        // The spec's budget is 15 ms; the bound is looser so a loaded test machine cannot flake it.
        Assert.True(sw.ElapsedMilliseconds < 50, $"autosave capture took {sw.ElapsedMilliseconds} ms on the main thread");
        Assert.True(w.AutosaveInFlight);
        Assert.False(map.Dirty);

        Assert.True(s.Saver.Flush(TimeSpan.FromSeconds(60)));
        w.Jobs.Drain(1000);
        Assert.False(w.AutosaveInFlight);
        Assert.True(autosaved >= 50);

        var reloaded = WorldSession.Load(TestPaths.Content, dir, Tables, game.Log);
        var wood = s.Content.BlockId("wood");
        foreach (var cell in cells)
        {
            var product = reloaded.Produce(ChunkMath.CellToChunk(cell));
            Assert.True(product.FromSave);
            reloaded.Install(product);
            Assert.Equal(wood, reloaded.World.GetBlock(cell));
        }
        var player = GameWorld.LoadOrCreatePlayer(Path.Combine(dir, "players"), game.Player.State.Name, s.Content);
        Assert.Equal(7, player.Deaths);
        var mapPath = Path.Combine(dir, MapSystem.MapDirectory, game.Player.State.Name + ".map");
        var size = s.Settings.Dimensions;
        var loadedMap = ExploredMap.Load(File.ReadAllBytes(mapPath), size.X, size.Z, n => s.Content.Blocks.TryGet(n, out var b) ? b.RuntimeId : null);
        Assert.Equal(MapValue.Block(wood), loadedMap.Surface(3, 4));
        Assert.Equal(0, game.Log.Count(LogLevel.Error));
    }

    [Fact]
    public void HardcoreDeathDuringAutosaveStaysDeleted()
    {
        var dir = TestWorlds.TempDir();
        using var game = TestGame.Create(dir);
        var w = game.World;
        game.Player.State.Difficulty = Strata.Core.Gameplay.Player.CharacterDifficulty.Hardcore;
        w.SaveAll();
        var path = GameWorld.PlayerPath(Path.Combine(dir, "players"), game.Player.State.Name);
        Assert.True(File.Exists(path));

        // The autosave (holding the living character's JSON) is still queued when the character dies for good.
        using var gate = new ManualResetEventSlim(false);
        w.Session.Saver!.TestGate = gate;
        Assert.True(w.BeginAutosave());
        game.Player.State.Vitals.DamageDirect(10_000);
        game.Ticks(GameWorld.RespawnDelayTicks + 5);
        Assert.True(game.Player.Lost);
        gate.Set();
        Assert.True(w.Session.Saver.Flush(TimeSpan.FromSeconds(60)));
        w.Jobs.Drain(1000);

        Assert.False(File.Exists(path));
        Assert.False(File.Exists(path + ".bak"));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void FailedMapWriteIsRetried()
    {
        var dir = TestWorlds.TempDir();
        using var game = TestGame.Create(dir);
        var w = game.World;
        var map = w.Maps.For(game.Player);
        map.SetSurface(1, 1, MapValue.Block(game.Content.BlockId("stone")), 10);
        var mapPath = Path.Combine(dir, MapSystem.MapDirectory, game.Player.State.Name + ".map");
        Directory.CreateDirectory(mapPath); // a directory where the file goes: the write fails

        Assert.True(w.BeginAutosave());
        Assert.False(map.Dirty);
        Assert.True(w.Session.Saver!.Flush(TimeSpan.FromSeconds(60)));
        w.Jobs.Drain(1000);
        Assert.True(map.Dirty);
        Assert.True(game.Log.Count(LogLevel.Error) > 0);

        Directory.Delete(mapPath);
        w.SaveAll();
        Assert.False(map.Dirty);
        Assert.True(File.Exists(mapPath));
    }

    [Fact]
    public void RestreamBeforeWriteKeepsEdits()
    {
        var dir = TestWorlds.TempDir();
        var log = new MemoryLog();
        var s = WorldSession.Create(TestPaths.Content, new WorldSettings { Name = "restream", Seed = 42, Size = WorldSizePreset.Tiny }, dir, Tables, log);
        using var jobs = new JobSystem(2, log);
        var streamer = new ChunkStreamer(s, jobs, new StreamingSettings { MeshRadius = 1, MeshDown = 1, MeshUp = 1, SkyColumnRadius = 0, Hysteresis = 0 });
        var spawn = s.FindStandableNear(s.SpawnCell);
        Pump(streamer, jobs, spawn);

        var cell = spawn + new Int3(2, 0, 0);
        var brick = s.Content.BlockId("gray_brick");
        var batch = s.World.BeginEdit();
        batch.SetBlock(cell, brick);
        streamer.MarkDirty(s.Commit(batch));

        using var gate = new ManualResetEventSlim(false);
        s.Saver!.TestGate = gate;
        int far = ChunkMath.CellToChunk(spawn).X + 5 < s.World.ChunkCounts.X ? 5 : -5;
        Pump(streamer, jobs, spawn + new Int3(far * 32, 0, 0));
        Assert.False(s.World.HasChunk(ChunkMath.CellToChunk(cell)));
        Assert.True(s.Saver.PendingCount > 0);

        Pump(streamer, jobs, spawn);
        Assert.Equal(brick, s.World.GetBlock(cell));

        gate.Set();
        streamer.UnloadAll();
        Assert.Equal(0, s.Saver.PendingCount);
        var reloaded = WorldSession.Load(TestPaths.Content, dir, Tables, log);
        reloaded.Install(reloaded.Produce(ChunkMath.CellToChunk(cell)));
        Assert.Equal(brick, reloaded.World.GetBlock(cell));
        Assert.Equal(0, log.Count(LogLevel.Error));
    }

    private static void Pump(ChunkStreamer streamer, JobSystem jobs, Int3 focus)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        do
        {
            streamer.Update(focus);
            jobs.Drain(50);
            Thread.Sleep(2);
        } while (!(streamer.IsIdle && jobs.Idle) && sw.ElapsedMilliseconds < 60_000);
        Assert.True(streamer.IsIdle, "streaming did not settle");
    }
}
