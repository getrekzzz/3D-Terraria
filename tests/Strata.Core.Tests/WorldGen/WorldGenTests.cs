using Strata.Core.Common;
using Strata.Core.Determinism;
using Strata.Core.Tests.Support;
using Strata.Core.World;
using Strata.Core.WorldGen;
using Strata.Core.WorldGen.Chunk;
using Strata.Core.WorldGen.Features;
using Strata.Core.WorldGen.Structures;

namespace Strata.Core.Tests.WorldGen;

public class WorldGenTests
{
    private static readonly Lazy<WorldGenerator> Tiny = new(() => new WorldGenerator(TestPaths.Content, new WorldSettings { Seed = 1234, Size = WorldSizePreset.Tiny }));

    /// <summary>
    /// Hash of a chunk's blocks (by string id, so adding block content that sorts earlier and shifts runtime ids does
    /// not change it), liquids and tile objects.
    /// </summary>
    internal static ulong ChunkHash(GeneratedChunk c)
    {
        var blocks = TestPaths.Content.Blocks;
        var ids = new ulong[blocks.Count];
        for (int b = 0; b < ids.Length; b++) ids[b] = Hash.String(blocks[b].Id);
        ulong h = Hash.String("chunk");
        for (int i = 0; i < ChunkMath.Volume; i++)
        {
            h = Hash.Combine(h, ids[c.Data.GetBlock(i)]);
            h = Hash.Combine(h, c.Data.Liquids?[i] ?? 0);
        }
        foreach (var o in c.Objects) h = Hash.Combine(Hash.Combine(h, Hash.String(o.Def.Id)), o.Anchor.X * 7919 + o.Anchor.Y * 104729 + o.Anchor.Z);
        return h;
    }

    [Fact]
    public void GenerationIsDeterministicAcrossGeneratorInstances()
    {
        var a = new WorldGenerator(TestPaths.Content, new WorldSettings { Seed = 77, Size = WorldSizePreset.Tiny });
        var b = new WorldGenerator(TestPaths.Content, new WorldSettings { Seed = 77, Size = WorldSizePreset.Tiny });
        Assert.Equal(a.Plan.Hash, b.Plan.Hash);
        var spawn = ChunkMath.CellToChunk(a.Plan.Spawn);
        foreach (var c in new[] { spawn, spawn + Int3.Down, spawn + Int3.Down * 3, new Int3(2, 4, 9) })
            Assert.Equal(ChunkHash(a.Generate(c)), ChunkHash(b.Generate(c)));
    }

    /// <summary>
    /// Golden chunks (seed 1234, tiny, generator version <see cref="GenVersion.Current"/>): the spawn forest, tundra,
    /// jungle and desert surfaces, two cavern chunks near spawn, one far out, one at the underworld's top, and the
    /// Dungeon's vault and keep. Any change to world generation output fails here. A deliberate change bumps GenVersion and updates these values in
    /// the same commit (keep at least three surface chunks: <see cref="GoldenChunksCoverTheSurface"/>).
    /// </summary>
    public static readonly TheoryData<int, int, int, ulong> Golden = new()
    {
        { 8, 10, 8, 1658534350731430268UL },
        { 4, 11, 8, 14139425615911629870UL },
        { 11, 11, 6, 571984169707453842UL },
        { 9, 11, 11, 5935961065046386009UL },
        { 8, 7, 8, 1248400004488476377UL },
        { 8, 4, 8, 15051355963523294477UL },
        { 2, 4, 9, 63819815911648630UL },
        { 12, 1, 5, 16627187558847016073UL },
        { 8, 7, 12, 7931888006566429743UL }, // the Dungeon's vault
        { 3, 11, 11, 54679113004194951UL }, // the Dungeon's keep
    };

    [Theory]
    [MemberData(nameof(Golden))]
    public void GeneratedChunksMatchTheirGoldenHashes(int x, int y, int z, ulong expected)
    {
        Assert.Equal(14, GenVersion.Current); // bumping the generator version means re-recording these hashes
        Assert.Equal(new Int3(256, 349, 256), Tiny.Value.Plan.Spawn);
        Assert.Equal(expected, ChunkHash(Tiny.Value.Generate(new Int3(x, y, z))));
    }

    /// <summary>At least three golden chunks hold the surface: the terrain surface crosses them, open above and solid below.</summary>
    [Fact]
    public void GoldenChunksCoverTheSurface()
    {
        var gen = Tiny.Value;
        int surfaceChunks = 0;
        foreach (var row in Golden)
        {
            var chunk = gen.Generate(new Int3((int)row[0], (int)row[1], (int)row[2]));
            int columns = 0;
            for (int lz = 0; lz < ChunkMath.Size; lz++)
                for (int lx = 0; lx < ChunkMath.Size; lx++)
                {
                    bool openTop = !gen.Content.Blocks[chunk.Data.GetBlock(ChunkMath.Index(new Int3(lx, ChunkMath.Size - 1, lz)))].IsSolid;
                    bool solidBottom = gen.Content.Blocks[chunk.Data.GetBlock(ChunkMath.Index(new Int3(lx, 0, lz)))].IsSolid;
                    if (openTop && solidBottom) columns++;
                }
            var origin = ChunkMath.ChunkOrigin(chunk.Coord);
            float surface = gen.Terrain.SurfaceHeight(origin.X + ChunkMath.Size / 2, origin.Z + ChunkMath.Size / 2);
            bool holdsSurface = surface >= origin.Y && surface < origin.Y + ChunkMath.Size;
            if (columns >= ChunkMath.Size * ChunkMath.Size / 2 && holdsSurface) surfaceChunks++;
        }
        Assert.True(surfaceChunks >= 3, $"only {surfaceChunks} golden chunks hold the surface");
    }

    [Fact]
    public void DifferentSeedsDiffer()
    {
        var other = new WorldGenerator(TestPaths.Content, new WorldSettings { Seed = 1235, Size = WorldSizePreset.Tiny });
        Assert.NotEqual(Tiny.Value.Plan.Hash, other.Plan.Hash);
    }

    public static IEnumerable<object[]> PurityChunks()
    {
        var gen = Tiny.Value;
        var spawn = ChunkMath.CellToChunk(gen.Plan.Spawn);
        int uw = gen.Plan.Bands.UnderworldTop / 32;
        yield return new object[] { spawn.X, spawn.Y, spawn.Z };
        yield return new object[] { spawn.X, spawn.Y - 1, spawn.Z };
        yield return new object[] { spawn.X + 3, spawn.Y - 4, spawn.Z - 2 };
        yield return new object[] { 5, uw, 9 };
        yield return new object[] { 1, spawn.Y - 1, 8 };
        yield return new object[] { 8, 7, 12 }; // the Dungeon's vault
        yield return new object[] { 6, 9, 13 }; // Dungeon stairs crossing a chunk face
        yield return new object[] { 3, 11, 11 }; // the Dungeon's keep
    }

    [Theory]
    [MemberData(nameof(PurityChunks))]
    public void PaddingMatchesNeighbourCores(int x, int y, int z)
    {
        // Every chunk computes a one-cell margin; its geometry (solid vs open) and liquids must equal what the
        // neighbour itself generates. Surface material conversions (turf, moss) in the margin's outermost
        // row cannot see beyond it and are never read, so only occupancy is compared.
        var gen = Tiny.Value;
        var c = new Int3(x, y, z);
        var ctx = gen.GenerateContext(c);
        foreach (var dir in Faces.Offsets)
        {
            var neighbour = gen.Generate(c + dir);
            var origin = ChunkMath.ChunkOrigin(c + dir);
            int mismatches = 0;
            var examples = new List<string>();
            for (int a = 0; a < ChunkMath.Size; a++)
                for (int b = 0; b < ChunkMath.Size; b++)
                {
                    // Cell just outside c in direction dir.
                    var local = dir.X != 0 ? new Int3(dir.X > 0 ? ChunkMath.Size : -1, a, b)
                        : dir.Y != 0 ? new Int3(a, dir.Y > 0 ? ChunkMath.Size : -1, b)
                        : new Int3(a, b, dir.Z > 0 ? ChunkMath.Size : -1);
                    var world = ChunkMath.ChunkOrigin(c) + local;
                    ushort padded = ctx.Get(world.X, world.Y, world.Z);
                    int cellIndex = ChunkMath.Index(world - origin);
                    ushort actual = neighbour.Data.GetBlock(cellIndex);
                    bool paddedSolid = gen.Content.Blocks[padded].IsSolid, actualSolid = gen.Content.Blocks[actual].IsSolid;
                    byte paddedLiquid = ctx.Liquids?[ctx.WorldIndex(world.X, world.Y, world.Z)] ?? 0;
                    byte actualLiquid = neighbour.Data.Liquids?[cellIndex] ?? 0;
                    if (paddedSolid != actualSolid || (paddedLiquid == 0) != (actualLiquid == 0))
                    {
                        mismatches++;
                        if (examples.Count < 5) examples.Add($"{world}: padded={gen.Content.Blocks[padded].Id} actual={gen.Content.Blocks[actual].Id}");
                    }
                }
            Assert.True(mismatches == 0, $"{mismatches} padding mismatches towards {dir}: {string.Join("; ", examples)}");
        }
    }

    [Fact]
    public void PlanContainsTheReferenceLayout()
    {
        var plan = Tiny.Value.Plan;
        var biomes = plan.Regions.Select(r => r.Biome).ToHashSet();
        Assert.Contains("tundra", biomes);
        Assert.Contains("jungle", biomes);
        Assert.Contains("desert", biomes);
        Assert.Contains("corruption", biomes);
        Assert.Contains(plan.Features.All, f => f is StructureFeature s && s.Kind == "dungeon" && s.Zone == "dungeon");
        Assert.Contains(plan.Features.All, f => f is IslandFeature);
        Assert.Contains(plan.Features.All, f => f.Kind == "worm");
        Assert.Equal(4, plan.OreChoices.Count);

        var tundra = plan.Regions.First(r => r.Biome == "tundra");
        var jungle = plan.Regions.First(r => r.Biome == "jungle");
        float cx = plan.Size.X / 2f, cz = plan.Size.Z / 2f;
        float dot = (tundra.CenterX - cx) * (jungle.CenterX - cx) + (tundra.CenterZ - cz) * (jungle.CenterZ - cz);
        Assert.True(dot < 0, "tundra and jungle must be on opposite sides of spawn");
    }

    [Fact]
    public void SpawnHasSolidGroundAndHeadroom()
    {
        var gen = Tiny.Value;
        var spawn = gen.Plan.Spawn;
        var chunkBelow = gen.Generate(ChunkMath.CellToChunk(spawn + Int3.Down * 2));
        var chunkAt = gen.Generate(ChunkMath.CellToChunk(spawn));
        ushort Block(Int3 cell)
        {
            var chunk = ChunkMath.CellToChunk(cell) == chunkAt.Coord ? chunkAt : chunkBelow;
            return chunk.Data.GetBlock(ChunkMath.Index(ChunkMath.CellToLocal(cell)));
        }
        int ground = spawn.Y;
        while (ground > spawn.Y - 12 && Block(new Int3(spawn.X, ground, spawn.Z)) == 0) ground--;
        Assert.NotEqual(0, Block(new Int3(spawn.X, ground, spawn.Z)));
        Assert.True(spawn.Y - ground < 8, "spawn floats too high above ground");
    }

    [Fact]
    public void SkyChunksAboveContentAreUniformAir()
    {
        var gen = Tiny.Value;
        var top = gen.Generate(new Int3(3, gen.Plan.Size.Y / 32 - 1, 3));
        Assert.True(top.Data.IsUniform);
        Assert.Equal(0, top.Data.UniformBlock);
    }
}
