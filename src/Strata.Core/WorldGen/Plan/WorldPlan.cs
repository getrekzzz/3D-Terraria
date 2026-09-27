using Strata.Core.Common;
using Strata.Core.Determinism;
using Strata.Core.World;
using Strata.Core.WorldGen.Features;

namespace Strata.Core.WorldGen.Plan;

/// <summary>
/// Global, deterministic layout of a world computed once from its seed (never saved — regenerated and
/// verified by <see cref="Hash"/>). Chunk generation reads it but never modifies it.
/// </summary>
public sealed class WorldPlan
{
    public const int HeightStep = 8;

    public WorldPlan(WorldSettings settings)
    {
        Settings = settings;
        Size = settings.Dimensions;
        Seeds = SeedTree.FromWorldSeed(settings.Seed);
        Bands = new LayerBands(Size.Y);
        Landness = new Grid2D(Size.X, Size.Z, HeightStep);
        Height = new Grid2D(Size.X, Size.Z, HeightStep);
        Ruggedness = new Grid2D(Size.X, Size.Z, HeightStep);
        ColumnTop = new int[(Size.X / ChunkMath.Size) * (Size.Z / ChunkMath.Size)];
    }

    public WorldSettings Settings { get; }
    public Int3 Size { get; }
    public SeedTree Seeds { get; }
    public LayerBands Bands { get; }

    /// <summary>0 = open ocean … 1 = inland (continental mask).</summary>
    public Grid2D Landness { get; }

    /// <summary>Surface height (cells) before fine detail.</summary>
    public Grid2D Height { get; }

    /// <summary>0–1: how much 3D overhang/cliff noise is applied near the surface.</summary>
    public Grid2D Ruggedness { get; }

    /// <summary>Biome regions, highest priority first.</summary>
    public List<BiomeRegion> Regions { get; } = new();

    public FeatureIndex Features { get; } = new();

    /// <summary>Ore alternate-group choices (group → chosen vein id).</summary>
    public Dictionary<string, string> OreChoices { get; } = new(StringComparer.Ordinal);

    /// <summary>Player spawn cell (on the surface near the centre).</summary>
    public Int3 Spawn { get; set; }

    /// <summary>Named places found while planning (see <see cref="LandmarkIds"/>), for residents and commands.</summary>
    public Dictionary<string, Int3> Landmarks { get; } = new(StringComparer.Ordinal);

    /// <summary>Direction (turns) from the centre towards the tundra; the jungle lies opposite.</summary>
    public float SnowAngle { get; set; }

    public string Hash { get; set; } = "";

    /// <summary>Highest y (exclusive) that can hold non-air content per chunk column (for skipping empty sky chunks).</summary>
    public int[] ColumnTop { get; }

    public int ColumnIndex(int chunkX, int chunkZ) => chunkZ * (Size.X / ChunkMath.Size) + chunkX;

    public void RaiseColumnTop(Aabb3i bounds)
    {
        var min = ChunkMath.CellToChunk(bounds.Min);
        var max = ChunkMath.CellToChunk(bounds.Max - Int3.One);
        int cw = Size.X / ChunkMath.Size, cd = Size.Z / ChunkMath.Size;
        for (int cz = Math.Max(0, min.Z); cz <= Math.Min(cd - 1, max.Z); cz++)
            for (int cx = Math.Max(0, min.X); cx <= Math.Min(cw - 1, max.X); cx++)
            {
                int i = ColumnIndex(cx, cz);
                ColumnTop[i] = Math.Max(ColumnTop[i], bounds.Max.Y);
            }
    }

    /// <summary>Raises each chunk column's content top to cover the terrain surface (plus overhang margin and sea level).</summary>
    public void RaiseTerrainColumnTops(Func<float, float, float> surfaceHeight)
    {
        int cw = Size.X / ChunkMath.Size, cd = Size.Z / ChunkMath.Size;
        for (int cz = 0; cz < cd; cz++)
            for (int cx = 0; cx < cw; cx++)
            {
                float top = Bands.SeaLevel + 2;
                for (int z = -4; z <= ChunkMath.Size + 4; z += 4)
                    for (int x = -4; x <= ChunkMath.Size + 4; x += 4)
                    {
                        float wx = cx * ChunkMath.Size + x, wz = cz * ChunkMath.Size + z;
                        float rug = Ruggedness.Sample(wx, wz);
                        top = MathF.Max(top, surfaceHeight(wx, wz) + 3f + Fields.TerrainField.OverhangAmplitude(rug) + 2f);
                    }
                int i = ColumnIndex(cx, cz);
                ColumnTop[i] = Math.Max(ColumnTop[i], (int)MathF.Ceiling(top));
            }
    }

    public bool IsAboveContent(Int3 chunk)
    {
        int cw = Size.X / ChunkMath.Size, cd = Size.Z / ChunkMath.Size;
        if (chunk.X < 0 || chunk.Z < 0 || chunk.X >= cw || chunk.Z >= cd) return chunk.Y * ChunkMath.Size > Bands.SeaLevel + 2;
        return chunk.Y * ChunkMath.Size >= ColumnTop[ColumnIndex(chunk.X, chunk.Z)];
    }

    public void AddFeature(WorldFeature feature)
    {
        Features.Add(feature);
        RaiseColumnTop(feature.Bounds);
    }
}

/// <summary>Landmarks the generator plans (content may refer to these by name).</summary>
public static class LandmarkIds
{
    /// <summary>Open ground just outside the crypt's gatehouse door.</summary>
    public const string DungeonGate = "dungeon_gate";

    public static readonly IReadOnlySet<string> All = new HashSet<string> { DungeonGate };
}
