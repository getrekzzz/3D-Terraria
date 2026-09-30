using System.Diagnostics;
using Strata.Core.Common;
using Strata.Core.Tests.Support;
using Strata.Core.Voxels;
using Strata.Core.World;
using Strata.Core.WorldGen;
using Strata.Core.WorldGen.Analysis;
using Strata.Core.WorldGen.Fields;

namespace Strata.Core.Tests.WorldGen;

public class SurfaceProfileTests
{
    private const int Ground = 40; // cells y < Ground are solid, so Top = 39

    /// <summary>A 96³ world of stone below <see cref="Ground"/> and air above.</summary>
    private static VoxelWorld Plane()
    {
        var db = TestPaths.Content;
        ushort stone = db.BlockId("stone");
        var world = new VoxelWorld(db, new Int3(96, 96, 96), stone);
        for (int cy = 0; cy < 3; cy++)
            for (int cz = 0; cz < 3; cz++)
                for (int cx = 0; cx < 3; cx++)
                {
                    var blocks = new ushort[ChunkMath.Volume];
                    for (int ly = 0; ly < ChunkMath.Size; ly++)
                        if (cy * ChunkMath.Size + ly < Ground) blocks.AsSpan(ly * ChunkMath.Area, ChunkMath.Area).Fill(stone);
                    world.AddChunk(new Chunk(new Int3(cx, cy, cz), ChunkData.Compact(blocks, null, null, null)));
                }
        return world;
    }

    private static SurfaceProfile Profile(VoxelWorld world) =>
        SurfaceProfile.FromChunks(world.Content, c => world.GetChunk(c)?.Capture(), 0, 0, 96, 96, 0, 95, (_, _) => Ground);

    [Fact]
    public void FlatPlaneHasNoSteps()
    {
        var profile = Profile(Plane());
        var stats = profile.Stats();

        Assert.All(profile.Top, t => Assert.Equal(Ground - 1, t));
        Assert.All(profile.Flags, f => Assert.Equal(SurfaceFlags.None, f));
        Assert.Equal(96 * 96, stats.Columns);
        Assert.Equal(2 * 96 * 95, stats.StepPairs);
        Assert.Equal(stats.StepPairs, stats.StepHistogram[0]);
        Assert.All(stats.StepHistogram.Skip(1), n => Assert.Equal(0, n));
        Assert.Equal(0, stats.StepMax);
        Assert.Equal(0, stats.LipPercent);
        Assert.Equal(0, stats.BreachPercent);
        Assert.Equal(0, stats.FloatingPer10k);
        Assert.Equal(0, stats.Roughness);
        Assert.Equal(0, stats.Relief);
        Assert.Empty(stats.ByBiome);
    }

    [Fact]
    public void ShelfOverAirIsALip()
    {
        var world = Plane();
        var batch = world.BeginEdit();
        // A 2-cell-thick shelf (y 38-39) over a 4-cell cavity at x, z 20..29.
        for (int x = 20; x < 30; x++)
            for (int z = 20; z < 30; z++)
                for (int y = Ground - 6; y < Ground - 2; y++) batch.SetBlock(x, y, z, 0);
        // A one-column hole 6 deep at (60, 60), with water in its bottom cell.
        for (int y = Ground - 6; y < Ground; y++) batch.SetBlock(60, y, 60, 0);
        batch.SetLiquid(60, Ground - 6, 60, LiquidCode.Full(LiquidType.Water));
        batch.Commit();

        var profile = Profile(world);
        Assert.Equal(SurfaceFlags.Lip | SurfaceFlags.Floating, profile.FlagsAt(25, 25));
        Assert.Equal(Ground - 1, profile.TopAt(25, 25));
        Assert.Equal(1, profile.FloatingCells[profile.Index(25, 25)]);
        Assert.Equal(SurfaceFlags.None, profile.FlagsAt(19, 25));
        Assert.Equal(SurfaceFlags.None, profile.FlagsAt(30, 25));

        Assert.Equal(Ground - 7, profile.TopAt(60, 60));
        Assert.Equal(SurfaceFlags.Breach | SurfaceFlags.Water, profile.FlagsAt(60, 60));
        Assert.Equal(SurfaceFlags.None, profile.FlagsAt(61, 60));

        var stats = profile.Stats();
        Assert.Equal(Math.Round(100.0 * 100 / (96 * 96), 4), stats.LipPercent);
        Assert.Equal(Math.Round(100.0 / (96 * 96), 4), stats.BreachPercent);
        Assert.Equal(4, stats.StepHistogram[4]); // the hole's four neighbours step down 6
        Assert.Equal(6, stats.StepMax);
        Assert.True(stats.Roughness > 0);

        var lipOnly = profile.Stats((x, z) => x is >= 20 and < 30 && z is >= 20 and < 30);
        Assert.Equal(100, lipOnly.Columns);
        Assert.Equal(100, lipOnly.LipPercent);
        Assert.Equal(10000, lipOnly.FloatingPer10k);
    }

    [Fact]
    public void MetricThresholdsArePinned()
    {
        // The definitions are frozen so reports stay comparable: pin every threshold on both sides.
        var world = Plane();
        var batch = world.BeginEdit();
        void Clear(int x, int z, int yFrom, int yTo)
        {
            for (int y = yFrom; y <= yTo; y++) batch.SetBlock(x, y, z, 0);
        }
        Clear(10, 10, 30, 33); // roof 6 thick over air
        Clear(12, 10, 29, 32); // roof 7 thick
        Clear(14, 10, 25, 26); // 2-cell gap under the cell 12 below Top
        Clear(16, 10, 24, 25); // 2-cell gap under the cell 13 below Top
        Clear(18, 10, 30, 30); // 1-cell gap under a 9-thick run
        Clear(20, 10, 36, 36); // 1-cell gap under a 3-thick run
        Clear(30, 30, Ground - 2, Ground - 1); // Top 3 below the surface
        Clear(32, 30, Ground - 3, Ground - 1); // Top 4 below the surface
        Clear(40, 40, Ground - SurfaceProfile.Footroom, Ground - 1); // nothing solid in the scan band
        batch.Commit();

        var profile = Profile(world);
        Assert.Equal(SurfaceFlags.Lip | SurfaceFlags.Floating, profile.FlagsAt(10, 10));
        Assert.Equal(SurfaceFlags.Floating, profile.FlagsAt(12, 10));
        Assert.Equal(SurfaceFlags.Floating, profile.FlagsAt(14, 10));
        Assert.Equal(1, profile.FloatingCells[profile.Index(14, 10)]);
        Assert.Equal(SurfaceFlags.None, profile.FlagsAt(16, 10));
        Assert.Equal(SurfaceFlags.None, profile.FlagsAt(18, 10));
        Assert.Equal(SurfaceFlags.Lip, profile.FlagsAt(20, 10));
        Assert.Equal(Ground - 3, profile.TopAt(30, 30));
        Assert.Equal(SurfaceFlags.None, profile.FlagsAt(30, 30));
        Assert.Equal(SurfaceFlags.Breach, profile.FlagsAt(32, 30));
        Assert.Equal(Ground - SurfaceProfile.Footroom - 1, profile.TopAt(40, 40));
        Assert.Equal(SurfaceFlags.Breach, profile.FlagsAt(40, 40));
    }

    [Fact]
    public void GeneratedWindowIsMeasurable()
    {
        // The scan band must cover the tallest overhang the density field can produce.
        Assert.True(TerrainField.OverhangAmplitude(1f) + 8f <= SurfaceProfile.Headroom);

        var gen = new WorldGenerator(TestPaths.Content, new WorldSettings { Seed = 1234, Size = WorldSizePreset.Tiny });
        var spawn = gen.Plan.Spawn;
        var sw = Stopwatch.StartNew();
        var profile = SurfaceProfile.Measure(gen, spawn.X - 48, spawn.Z - 48, 96, 96, threads: Environment.ProcessorCount);
        var stats = profile.Stats();
        sw.Stop();

        Assert.True(sw.Elapsed.TotalSeconds < 5, $"measuring took {sw.Elapsed.TotalSeconds:F1} s");
        Assert.Equal(96 * 96, stats.Columns);
        Assert.True(profile.ChunksGenerated > 0);
        Assert.Equal(stats.StepPairs, stats.StepHistogram.Sum());
        foreach (var v in new[] { stats.LipPercent, stats.FloatingPer10k, stats.BreachPercent, stats.WaterPercent, stats.Roughness, stats.Relief })
            Assert.True(double.IsFinite(v) && v >= 0);
        Assert.InRange(stats.StepP95, 0, stats.StepP99);
        Assert.InRange(stats.StepP99, 0, stats.StepMax);
        Assert.NotEmpty(stats.ByBiome);
        Assert.Equal(stats.Columns, stats.ByBiome.Values.Sum(b => b.Columns) + SmallBiomeColumns(profile, stats));
        int standing = profile.Index(spawn.X, spawn.Z);
        Assert.InRange(profile.Top[standing], (int)profile.Surface[standing] - SurfaceProfile.Footroom, (int)profile.Surface[standing] + SurfaceProfile.Headroom);

        // Threading only changes speed, never the result.
        var single = SurfaceProfile.Measure(gen, spawn.X - 16, spawn.Z - 16, 40, 40);
        var parallel = SurfaceProfile.Measure(gen, spawn.X - 16, spawn.Z - 16, 40, 40, threads: 4);
        Assert.Equal(single.Top, parallel.Top);
        Assert.Equal(single.Flags, parallel.Flags);
        Assert.Equal(single.FloatingCells, parallel.FloatingCells);
        Assert.Equal(single.Biome, parallel.Biome);
    }

    private static int SmallBiomeColumns(SurfaceProfile profile, TerrainStats stats) =>
        profile.Biome!.GroupBy(b => b).Where(g => !stats.ByBiome.ContainsKey(profile.Content.Biomes[g.Key].Id)).Sum(g => g.Count());
}
