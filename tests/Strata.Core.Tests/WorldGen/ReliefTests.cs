using Strata.Core.Tests.Support;
using Strata.Core.World;
using Strata.Core.WorldGen;
using Strata.Core.WorldGen.Passes;
using Strata.Core.WorldGen.Plan;

namespace Strata.Core.Tests.WorldGen;

/// <summary>The planned surface relief: soft rolling landforms, neither knife-edged nor flattened.</summary>
public class ReliefTests
{
    private static readonly Lazy<WorldGenerator> Small = new(() => new WorldGenerator(TestPaths.Content, new WorldSettings { Seed = 1234, Size = WorldSizePreset.Small }));

    /// <summary>
    /// Away from mountain ranges the inland surface never gets steeper than 0.9 cells per cell (before this relief it
    /// reached 1.5: ridged snow crests, jungle spikes and stepped biome borders). The coast, where the relief fades
    /// into the beach over the landness ramp, is left out.
    /// </summary>
    [Fact]
    public void PlanSlopesAreGentle()
    {
        var gen = Small.Value;
        var plan = gen.Plan;
        var terrain = gen.Terrain;
        var nearMountains = MountainReach(gen);
        float steepest = 0f;
        int sx = 0, sz = 0, measured = 0;
        for (int z = 8; z < plan.Size.Z - 8; z += 4)
            for (int x = 8; x < plan.Size.X - 8; x += 4)
            {
                if (plan.Landness.Sample(x, z) < 0.98f || nearMountains[(z / WorldPlan.HeightStep) * plan.Landness.Width + x / WorldPlan.HeightStep]) continue;
                float gx = (terrain.SurfaceHeight(x + 1, z) - terrain.SurfaceHeight(x - 1, z)) * 0.5f;
                float gz = (terrain.SurfaceHeight(x, z + 1) - terrain.SurfaceHeight(x, z - 1)) * 0.5f;
                float slope = MathF.Sqrt(gx * gx + gz * gz);
                measured++;
                if (slope <= steepest) continue;
                (steepest, sx, sz) = (slope, x, z);
            }
        Assert.True(measured > 50_000, $"only {measured} inland columns away from mountains");
        Assert.True(steepest < 0.9f, $"slope {steepest:F2} at ({sx},{sz}) in {gen.Content.Biomes[gen.Biomes.SurfaceBiome(sx, sz)].Id}");
    }

    /// <summary>Smoothing must not flatten the land: the hills around spawn keep a real height spread.</summary>
    [Fact]
    public void RollingHillsKept()
    {
        var gen = Small.Value;
        var spawn = gen.Plan.Spawn;
        double sum = 0, sumSq = 0;
        int n = 0;
        for (int z = spawn.Z - 400; z <= spawn.Z + 400; z += 2)
            for (int x = spawn.X - 400; x <= spawn.X + 400; x += 2)
            {
                int dx = x - spawn.X, dz = z - spawn.Z, d2 = dx * dx + dz * dz;
                if (d2 < 150 * 150 || d2 > 400 * 400) continue;
                double h = gen.Terrain.SurfaceHeight(x, z);
                sum += h;
                sumSq += h * h;
                n++;
            }
        double mean = sum / n, std = Math.Sqrt(sumSq / n - mean * mean);
        Assert.True(std >= 4.0, $"surface height std-dev {std:F2} cells in the ring around spawn");
    }

    /// <summary>
    /// Plan-grid samples within reach of a mountain range: the mask dilated by 6 samples (48 cells), the reach of the
    /// relief blur plus the B-spline.
    /// </summary>
    private static bool[] MountainReach(WorldGenerator gen)
    {
        var relief = gen.Passes.OfType<BiomeMasksPass>().Single().Relief!;
        var grid = gen.Plan.Landness;
        int w = grid.Width, d = grid.Depth, reach = 6;
        var mask = new bool[w * d];
        for (int gz = 0; gz < d; gz++)
            for (int gx = 0; gx < w; gx++)
                mask[gz * w + gx] = relief.MountainMask(gx * grid.Step, gz * grid.Step) > 0f;
        var near = new bool[w * d];
        for (int gz = 0; gz < d; gz++)
            for (int gx = 0; gx < w; gx++)
            {
                if (!mask[gz * w + gx]) continue;
                for (int z = Math.Max(0, gz - reach); z <= Math.Min(d - 1, gz + reach); z++)
                    for (int x = Math.Max(0, gx - reach); x <= Math.Min(w - 1, gx + reach); x++)
                        near[z * w + x] = true;
            }
        return near;
    }
}
