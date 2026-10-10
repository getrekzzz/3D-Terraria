using Strata.Core.Common;
using Strata.Core.Tests.Support;
using Strata.Core.Voxels;
using Strata.Core.World;
using Strata.Core.WorldGen;
using Strata.Core.WorldGen.Analysis;
using Strata.Core.WorldGen.Features;
using Strata.Core.WorldGen.Fields;
using Strata.Core.WorldGen.Plan;

namespace Strata.Core.Tests.WorldGen;

/// <summary>
/// The surface is predictable: a heightfield with sub-cell jitter outside rare cliff zones, no random fins, blobs or
/// pits near spawn, noise caves that keep a roof, and worm tunnels that open as a few cave mouths.
/// </summary>
public class TerrainShapeTests
{
    private static readonly Lazy<WorldGenerator> Small = new(() => new WorldGenerator(TestPaths.Content, new WorldSettings { Seed = 1234, Size = WorldSizePreset.Small }));
    private static readonly Lazy<WorldGenerator> Tiny = new(() => new WorldGenerator(TestPaths.Content, new WorldSettings { Seed = 1234, Size = WorldSizePreset.Tiny }));

    /// <summary>Outside cliff zones the density falls with height through the whole noise band: one surface per column, no overhangs.</summary>
    [Fact]
    public void DensityIsMonotoneOutsideCliffZones()
    {
        var gen = Small.Value;
        var terrain = gen.Terrain;
        var rng = new Random(5);
        int columns = 0;
        for (int attempt = 0; columns < 20_000 && attempt < 200_000; attempt++)
        {
            int x = rng.Next(gen.Plan.Size.X), z = rng.Next(gen.Plan.Size.Z);
            if (gen.Plan.Landness.Sample(x, z) < 0.5f) continue;
            float cliff = terrain.Cliff(x, z);
            if (cliff >= 0.05f) continue;
            columns++;
            float h = terrain.SurfaceHeight(x, z);
            float amp = TerrainField.OverhangAmplitude(terrain.Ruggedness(x, z), cliff);
            Assert.True(amp <= 2.5f, $"amplitude {amp:F2} outside a cliff zone at ({x},{z})");
            int lo = (int)MathF.Floor(h - amp) - 3, hi = (int)MathF.Ceiling(h + amp) + 3;
            int signChanges = 0;
            float prev = terrain.Density(x, lo, z, h, amp, cliff);
            for (int y = lo + 1; y <= hi; y++)
            {
                float d = terrain.Density(x, y, z, h, amp, cliff);
                bool bothInBand = MathF.Abs(h - y) < amp + 1f && MathF.Abs(h - (y - 1)) < amp + 1f;
                if (bothInBand) Assert.True(d < prev, $"density rises from y {y - 1} to {y} at ({x},{z})");
                if ((d > 0f) != (prev > 0f)) signChanges++;
                prev = d;
            }
            Assert.Equal(1, signChanges);
        }
        Assert.Equal(20_000, columns);
    }

    /// <summary>Cliff zones cover a few percent of the land and never the spawn area.</summary>
    [Fact]
    public void CliffZonesAreRare()
    {
        var plan = Small.Value.Plan;
        var cliffs = plan.Cliffs;
        int land = 0, cliff = 0;
        float cx = plan.Size.X * 0.5f, cz = plan.Size.Z * 0.5f;
        for (int gz = 0; gz < cliffs.Depth; gz++)
            for (int gx = 0; gx < cliffs.Width; gx++)
            {
                float v = cliffs[gx, gz];
                float dx = gx * cliffs.Step - cx, dz = gz * cliffs.Step - cz;
                if (dx * dx + dz * dz < 140f * 140f) Assert.True(v <= 0f, $"cliff {v:F2} {MathF.Sqrt(dx * dx + dz * dz):F0} cells from spawn");
                if (plan.Landness[gx, gz] < 0.5f) continue;
                land++;
                if (v > 0.25f) cliff++;
            }
        double percent = 100.0 * cliff / land;
        Assert.InRange(percent, 2.0, 15.0);
    }

    /// <summary>
    /// Inside strong cliff zones the terraced overhang noise raises real walls: steps of 4+ cells between neighbouring
    /// columns, and a few undercut columns (more than one solid run), but still mostly walkable ground.
    /// </summary>
    [Fact]
    public void CliffZonesHaveSteepWalls()
    {
        var gen = Small.Value;
        var terrain = gen.Terrain;
        var cliffs = gen.Plan.Cliffs;
        int columns = 0, undercut = 0, pairs = 0, walls = 0;
        for (int i = 0; i < cliffs.Values.Length; i += 7)
        {
            if (cliffs.Values[i] < 0.4f) continue;
            int x0 = i % cliffs.Width * cliffs.Step, z0 = i / cliffs.Width * cliffs.Step;
            for (int z = z0; z < z0 + 16; z++)
            {
                int previous = int.MinValue;
                for (int x = x0; x < x0 + 16; x++)
                {
                    var (top, runs) = DensityColumn(terrain, x, z);
                    columns++;
                    if (runs > 1) undercut++;
                    if (previous != int.MinValue)
                    {
                        pairs++;
                        if (Math.Abs(top - previous) >= 4) walls++;
                    }
                    previous = top;
                }
            }
        }
        Assert.True(columns > 10_000, $"only {columns} cliff-zone columns");
        double wallPercent = 100.0 * walls / pairs, undercutPercent = 100.0 * undercut / columns;
        Assert.True(wallPercent >= 0.5, $"walls {wallPercent:F2}% of neighbour pairs");
        Assert.InRange(undercutPercent, 0.1, 5.0);
    }

    /// <summary>Top solid cell and number of separate solid runs of the density field in a column's noise band.</summary>
    private static (int Top, int Runs) DensityColumn(TerrainField terrain, int x, int z)
    {
        float h = terrain.SurfaceHeight(x, z), cliff = terrain.Cliff(x, z);
        float amp = TerrainField.OverhangAmplitude(terrain.Ruggedness(x, z), cliff);
        int lo = (int)MathF.Floor(h - amp) - 2, top = lo, runs = 0;
        bool above = false;
        for (int y = (int)MathF.Ceiling(h + amp) + 2; y >= lo; y--)
        {
            bool solid = terrain.Density(x, y, z, h, amp, cliff) > 0f;
            if (solid && !above && runs++ == 0) top = y;
            above = solid;
        }
        return (top, runs);
    }

    /// <summary>
    /// Around spawn the ground is a plain heightfield: no lips (overhangs, thin roofs) outside the planned features
    /// (cave mouths, structures), few open pits even counting the cave mouths, and small steps.
    /// </summary>
    [Fact]
    public void SpawnAreaIsAHeightfield()
    {
        var gen = Tiny.Value;
        var spawn = gen.Plan.Spawn;
        var profile = SurfaceProfile.Measure(gen, spawn.X - 80, spawn.Z - 80, 160, 160, threads: Environment.ProcessorCount);
        var all = profile.Stats();
        // A feature's footprint near the surface: the Dungeon's box spans its deep halls under spawn too, so it counts
        // op by op (the keep, the shaft), and only boxes that reach the surface band mark their columns.
        var boxes = new List<Aabb3i>();
        foreach (var f in gen.Plan.Features.All)
            if (f is Strata.Core.WorldGen.Structures.Dungeon.DungeonFeature d) foreach (var op in d.Blueprint.Ops) boxes.Add(op.Bounds);
            else boxes.Add(f.Bounds);
        var terrain = profile.Stats((x, z) =>
        {
            float top = gen.Terrain.SurfaceHeight(x, z);
            return !boxes.Any(b => x >= b.Min.X && x < b.Max.X && z >= b.Min.Z && z < b.Max.Z && b.Max.Y >= top - 12 && b.Min.Y <= top + 12);
        });
        Assert.True(terrain.Columns > 160 * 160 / 2, $"only {terrain.Columns} columns away from features");
        Assert.Equal(0.0, terrain.LipPercent);
        Assert.Equal(0.0, terrain.BreachPercent);
        Assert.True(all.BreachPercent < 1.0, $"breach {all.BreachPercent:F2}%");
        Assert.True(all.StepP99 <= 2, $"p99 step {all.StepP99}");
    }

    /// <summary>
    /// Away from planned features (worms, shafts, structures, lakes) the noise caves never break the surface: the four
    /// cells under the top solid cell are solid, in the spawn area and around the strongest cliff zone alike (except
    /// where the terrain itself is undercut).
    /// </summary>
    [Fact]
    public void NoiseCavesKeepARoof()
    {
        var gen = Small.Value;
        var cliffs = gen.Plan.Cliffs;
        int best = 0;
        for (int i = 1; i < cliffs.Values.Length; i++)
            if (cliffs.Values[i] > cliffs.Values[best]) best = i;
        Assert.True(cliffs.Values[best] > 0.5f, "no cliff zone to test");
        int bx = best % cliffs.Width * cliffs.Step, bz = best / cliffs.Width * cliffs.Step;
        var spawn = gen.Plan.Spawn;
        int checkedColumns = RoofCheck(gen, spawn.X - 64, spawn.Z - 64, 128) + RoofCheck(gen, bx - 64, bz - 64, 128);
        Assert.True(checkedColumns > 10_000, $"only {checkedColumns} columns away from features");
    }

    private static int RoofCheck(WorldGenerator gen, int x0, int z0, int size)
    {
        var features = gen.Plan.Features.All;
        var blocks = gen.Content.Blocks;
        x0 = Math.Clamp(x0, 0, gen.Plan.Size.X - size);
        z0 = Math.Clamp(z0, 0, gen.Plan.Size.Z - size);
        int checkedColumns = 0;
        for (int cz = ChunkMath.ToChunk(z0); cz <= ChunkMath.ToChunk(z0 + size - 1); cz++)
            for (int cx = ChunkMath.ToChunk(x0); cx <= ChunkMath.ToChunk(x0 + size - 1); cx++)
            {
                var origin = new Int3(cx * ChunkMath.Size, 0, cz * ChunkMath.Size);
                var near = features.Where(f => f.Bounds.Max.X > origin.X && f.Bounds.Min.X < origin.X + ChunkMath.Size
                    && f.Bounds.Max.Z > origin.Z && f.Bounds.Min.Z < origin.Z + ChunkMath.Size).ToList();
                var chunks = new Dictionary<int, ChunkData>();
                ChunkData At(int y) => chunks.TryGetValue(y >> ChunkMath.Shift, out var c) ? c
                    : chunks[y >> ChunkMath.Shift] = gen.Generate(new Int3(cx, y >> ChunkMath.Shift, cz)).Data;
                for (int lz = 0; lz < ChunkMath.Size; lz++)
                    for (int lx = 0; lx < ChunkMath.Size; lx++)
                    {
                        int x = origin.X + lx, z = origin.Z + lz;
                        float h = gen.Terrain.SurfaceHeight(x, z);
                        // The cave network runs everywhere under the ground; only its entrances come near the surface.
                        if (near.Any(f => x >= f.Bounds.Min.X && x < f.Bounds.Max.X && z >= f.Bounds.Min.Z && z < f.Bounds.Max.Z
                            && (f is not (Strata.Core.WorldGen.Caves.CaveRoomFeature or Strata.Core.WorldGen.Caves.CavePassageFeature) || f.Bounds.Max.Y >= h - 8f))) continue;
                        if (gen.Plan.Landness.Sample(x, z) < 0.9f || h < gen.Plan.Bands.SeaLevel + 2) continue;
                        if (DensityColumn(gen.Terrain, x, z).Runs > 1) continue; // a deliberate cliff-zone undercut
                        int top = (int)MathF.Ceiling(h + TerrainField.OverhangAmplitude(1f, 1f)) + 2;
                        while (!blocks[At(top).GetBlock(ChunkMath.Index(new Int3(lx, top & ChunkMath.Mask, lz)))].IsSolid) top--;
                        for (int y = top - 1; y >= top - 4; y--)
                            Assert.True(blocks[At(y).GetBlock(ChunkMath.Index(new Int3(lx, y & ChunkMath.Mask, lz)))].IsSolid,
                                $"open cell {top - y} under the top at ({x},{top},{z})");
                        checkedColumns++;
                    }
            }
        return checkedColumns;
    }

    /// <summary>About a third of the worm tunnels open as cave mouths at the surface; the rest start well underground.</summary>
    [Fact]
    public void WormMouthsAreFew()
    {
        var gen = Small.Value;
        int mouths = 0, buried = 0;
        CapsuleFeature? previous = null;
        foreach (var w in gen.Plan.Features.OfKind<CapsuleFeature>("worm"))
        {
            bool chainStart = previous == null || previous.B != w.A;
            previous = w;
            if (!chainStart) continue;
            float below = gen.Terrain.SurfaceHeight(w.A.X, w.A.Z) - w.A.Y;
            if (below <= 2f) mouths++;
            else
            {
                Assert.True(below >= 12f, $"worm starts {below:F1} cells under the surface at {w.A}");
                buried++;
            }
        }
        double share = (double)mouths / (mouths + buried);
        Assert.True(mouths + buried > 100, $"only {mouths + buried} worms");
        Assert.InRange(share, 0.2, 0.4);
    }
}
