using Strata.Core.Common;
using Strata.Core.Diagnostics;
using Strata.Core.Meshing;
using Strata.Core.Session;
using Strata.Core.Streaming;
using Strata.Core.Tests.Support;
using Strata.Core.Voxels;
using Strata.Core.World;

namespace Strata.Core.Tests.Session;

public class StreamingTests
{
    private static readonly RenderTables Tables = new(TestPaths.Content, _ => 0);

    private static void Pump(ChunkStreamer streamer, JobSystem jobs, Int3 focus, int maxMs = 60_000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        do
        {
            streamer.Update(focus);
            jobs.Drain(50);
            Thread.Sleep(2);
        } while (!(streamer.IsIdle && jobs.Idle) && sw.ElapsedMilliseconds < maxMs);
        Assert.True(streamer.IsIdle, "streaming did not settle");
    }

    [Fact]
    public void StreamsEditsSavesAndReloads()
    {
        var dir = TestWorlds.TempDir();
        var log = new MemoryLog();
        var settings = new WorldSettings { Name = "t", Seed = 42, Size = WorldSizePreset.Tiny };
        var session = WorldSession.Create(TestPaths.Content, settings, dir, Tables, log);
        using var jobs = new JobSystem(3, log);
        var streamer = new ChunkStreamer(session, jobs, new StreamingSettings { MeshRadius = 1, MeshDown = 1, MeshUp = 1, SkyColumnRadius = 1 });
        var meshed = new HashSet<Int3>();
        streamer.MeshReady += r => meshed.Add(r.Coord);

        var spawn = session.FindStandableNear(session.SpawnCell);
        Pump(streamer, jobs, spawn);
        spawn = session.FindStandableNear(session.SpawnCell);
        Assert.True(session.IsStandable(spawn.X, spawn.Y, spawn.Z, 4), "no standable spawn after streaming");
        Assert.Contains(ChunkMath.CellToChunk(spawn), meshed);
        Assert.Equal(0, log.Count(LogLevel.Error));

        // Dig out the ground under spawn and place a crisp brick.
        var ground = spawn + Int3.Down;
        var brick = session.Content.BlockId("stone_brick");
        var batch = session.World.BeginEdit();
        batch.SetBlock(ground, 0);
        batch.SetBlock(spawn + new Int3(2, 0, 0), brick, 0, CellFlags.PlayerPlaced);
        var dirty = session.Commit(batch);
        Assert.Contains(ChunkMath.CellToChunk(ground), dirty);
        meshed.Clear();
        streamer.MarkDirty(dirty);
        Pump(streamer, jobs, spawn);
        Assert.Contains(ChunkMath.CellToChunk(ground), meshed);

        session.SaveAll();
        streamer.UnloadAll();

        var reloaded = WorldSession.Load(TestPaths.Content, dir, Tables, log);
        var product = reloaded.Produce(ChunkMath.CellToChunk(ground));
        Assert.True(product.FromSave);
        reloaded.Install(product);
        var brickChunk = reloaded.Produce(ChunkMath.CellToChunk(spawn + new Int3(2, 0, 0)));
        reloaded.Install(brickChunk);
        Assert.Equal(0, reloaded.World.GetBlock(ground));
        Assert.Equal(brick, reloaded.World.GetBlock(spawn + new Int3(2, 0, 0)));
        Assert.True((reloaded.World.GetFlags(spawn.X + 2, spawn.Y, spawn.Z) & CellFlags.PlayerPlaced) != 0);
        Assert.Equal(0, log.Count(LogLevel.Error));
    }

    [Fact]
    public void ClockCyclesDayAndNight()
    {
        var clock = new Simulation.GameClock();
        clock.SetHour(12);
        Assert.True(clock.IsDay);
        clock.SetHour(22);
        Assert.False(clock.IsDay);
        long day = clock.Day;
        clock.Advance(Simulation.GameClock.TicksPerDay);
        Assert.Equal(day + 1, clock.Day);
        Assert.Equal((int)((day + 1) % 8), clock.MoonPhase);
    }
}
