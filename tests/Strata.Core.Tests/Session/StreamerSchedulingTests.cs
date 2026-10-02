using Strata.Core.Common;
using Strata.Core.Diagnostics;
using Strata.Core.Meshing;
using Strata.Core.Session;
using Strata.Core.Streaming;
using Strata.Core.Tests.Support;
using Strata.Core.World;

namespace Strata.Core.Tests.Session;

public class StreamerSchedulingTests
{
    private static readonly RenderTables Tables = new(TestPaths.Content, _ => 0);

    private static (WorldSession Session, JobSystem Jobs, ChunkStreamer Streamer) Create(int meshRadius = 1, int maxMeshes = 0)
    {
        var log = new MemoryLog();
        var settings = new WorldSettings { Name = "t", Seed = 42, Size = WorldSizePreset.Tiny };
        var session = WorldSession.Create(TestPaths.Content, settings, TestWorlds.TempDir(), Tables, log);
        var jobs = new JobSystem(3, log);
        var s = new StreamingSettings
        {
            MeshRadius = meshRadius, MeshDown = 1, MeshUp = 1, SkyColumnRadius = 1,
            MaxMeshesInFlight = maxMeshes > 0 ? maxMeshes : new StreamingSettings().MaxMeshesInFlight,
        };
        var streamer = new ChunkStreamer(session, jobs, s);
        return (session, jobs, streamer);
    }

    private static void Pump(ChunkStreamer streamer, JobSystem jobs, Int3 focus, int maxMs = 60_000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        do
        {
            streamer.Update(focus);
            jobs.Drain(50);
            Thread.Sleep(2);
        } while (!(streamer.IsIdle && jobs.Idle) && sw.ElapsedMilliseconds < maxMs);
        Assert.True(streamer.IsIdle, "streaming did not settle: " + streamer.DebugState());
    }

    private static void AssertCounters(ChunkStreamer streamer)
    {
        var (loaded, meshed) = streamer.RecountForTests();
        Assert.Equal(loaded, streamer.LoadedCount);
        Assert.Equal(meshed, streamer.MeshedCount);
    }

    [Fact]
    public void SteadyStateDoesNotAllocate()
    {
        var (session, jobs, streamer) = Create();
        using var _ = jobs;
        var spawn = session.FindStandableNear(session.SpawnCell);
        Pump(streamer, jobs, spawn);
        for (int i = 0; i < 5; i++) streamer.Update(spawn); // warm up (JIT, lazily created state)

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 200; i++) streamer.Update(spawn);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated < 2048, $"steady-state Update allocated {allocated} bytes");
    }

    [Fact]
    public void CountersMatchRecount()
    {
        var (session, jobs, streamer) = Create();
        using var _ = jobs;
        var spawn = session.FindStandableNear(session.SpawnCell);
        Pump(streamer, jobs, spawn);
        Assert.True(streamer.LoadedCount > 0 && streamer.MeshedCount > 0);
        AssertCounters(streamer);

        // Move the focus three chunks along X (loads, unloads, hysteresis).
        var moved = spawn + new Int3(3 * ChunkMath.Size, 0, 0);
        if (!session.World.InBounds(moved.X, moved.Y, moved.Z)) moved = spawn - new Int3(3 * ChunkMath.Size, 0, 0);
        Pump(streamer, jobs, moved);
        AssertCounters(streamer);

        // Edit next to the focus and remesh.
        var batch = session.World.BeginEdit();
        batch.SetBlock(moved + Int3.Down, 0);
        streamer.MarkDirty(session.Commit(batch));
        Pump(streamer, jobs, moved);
        AssertCounters(streamer);

        streamer.UnloadAll();
        AssertCounters(streamer);
        Assert.Equal(0, streamer.LoadedCount);
        Assert.Equal(0, streamer.MeshedCount);
    }

    [Fact]
    public void UrgentEditsMeshFirst()
    {
        var (session, jobs, streamer) = Create(meshRadius: 2);
        using var _ = jobs;
        var spawn = session.FindStandableNear(session.SpawnCell);
        Pump(streamer, jobs, spawn);
        var focus = ChunkMath.CellToChunk(spawn);

        // Dirty every meshed chunk in distance order, then urgently dirty the farthest one.
        var all = new List<Int3>();
        for (int dy = -1; dy <= 1; dy++)
            for (int dz = -2; dz <= 2; dz++)
                for (int dx = -2; dx <= 2; dx++)
                    if (streamer.IsMeshed(focus + new Int3(dx, dy, dz))) all.Add(focus + new Int3(dx, dy, dz));
        var far = all.OrderByDescending(c => (c - focus).ChebyshevLength).ThenBy(c => c.X).First();
        var order = new List<Int3>();
        streamer.MeshScheduled = order.Add;
        streamer.MarkDirty(all, urgent: false);
        streamer.MarkDirty(new[] { far }, urgent: true);
        Assert.NotEmpty(order);
        Assert.Equal(far, order[0]);

        // The rest follow nearest-first.
        Pump(streamer, jobs, spawn);
        Assert.Equal(all.Count, order.Distinct().Count());
        Assert.Equal(focus, order[1]);
    }

    [Fact]
    public void LightRemeshesFollowEditsButPrecedeDistanceOrder()
    {
        var (session, jobs, streamer) = Create(meshRadius: 2, maxMeshes: 1);
        using var _ = jobs;
        var spawn = session.FindStandableNear(session.SpawnCell);
        Assert.False(streamer.IsSettledNear(spawn));
        Pump(streamer, jobs, spawn);
        Assert.True(streamer.IsSettledNear(spawn));
        var focus = ChunkMath.CellToChunk(spawn);

        // One mesh job at a time: the first urgent request takes the slot, the rest queue by priority.
        var blocker = focus + new Int3(0, 0, 2);
        var near = focus;
        var light = focus + new Int3(2, 0, 0);
        var edit = focus + new Int3(-2, 0, 0);
        Assert.True(streamer.IsMeshed(light) && streamer.IsMeshed(edit) && streamer.IsMeshed(blocker));
        var order = new List<Int3>();
        streamer.MeshScheduled = order.Add;
        streamer.MarkDirty(new[] { blocker }, urgent: true);
        streamer.MarkDirty(new[] { near }, urgent: false);
        streamer.MarkDirty(new[] { light }, urgent: true, ChunkStreamer.LightPriority);
        streamer.MarkDirty(new[] { edit }, urgent: true);
        Pump(streamer, jobs, spawn);
        Assert.Equal(new[] { blocker, edit, light, near }, order);
    }

    [Fact]
    public void TieBreakOrderIsDeterministic()
    {
        var orders = new List<List<Int3>>();
        for (int run = 0; run < 2; run++)
        {
            var (session, jobs, streamer) = Create();
            using var _ = jobs;
            var spawn = session.FindStandableNear(session.SpawnCell);
            Pump(streamer, jobs, spawn);
            var order = new List<Int3>();
            streamer.MeshScheduled = order.Add;
            var all = new List<Int3>();
            var focus = ChunkMath.CellToChunk(spawn);
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++) all.Add(focus + new Int3(dx, 0, dz));
            streamer.MarkDirty(all, urgent: true);
            orders.Add(order.ToList());
        }
        Assert.Equal(orders[0], orders[1]);
    }
}
