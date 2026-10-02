using System.Runtime.CompilerServices;
using Strata.Core.Common;
using Strata.Core.Determinism;
using Strata.Core.Voxels;
using Strata.Core.Voxels.TileObjects;

namespace Strata.Core.WorldGen.Chunk;

/// <summary>
/// Working state while generating one chunk. Passes operate on a padded box (1 cell of margin on every
/// side) so neighbour-dependent decisions (e.g. "is the cell above air?") are computed consistently by
/// both chunks sharing a border, without ever reading another chunk's buffer.
/// </summary>
public sealed class ChunkGenContext
{
    public const int Pad = 1;
    public const int Size = ChunkMath.Size + 2 * Pad;
    public const int Volume = Size * Size * Size;

    public ChunkGenContext(WorldGenerator generator, Int3 coord)
    {
        Gen = generator;
        Coord = coord;
        ChunkOrigin = ChunkMath.ChunkOrigin(coord);
        Origin = ChunkOrigin - Int3.One * Pad;
        Blocks = new ushort[Volume];
        SurfaceHeight = new float[Size * Size];
        Ruggedness = new float[Size * Size];
        Cliff = new float[Size * Size];
        OverhangAmp = new float[Size * Size];
        SoilDepth = new float[Size * Size];
        Biome = new byte[Volume];
        for (int lz = 0; lz < Size; lz++)
            for (int lx = 0; lx < Size; lx++)
            {
                int wx = Origin.X + lx, wz = Origin.Z + lz;
                int c = lz * Size + lx;
                SurfaceHeight[c] = generator.Terrain.SurfaceHeight(wx, wz);
                Ruggedness[c] = generator.Terrain.Ruggedness(wx, wz);
                Cliff[c] = generator.Terrain.Cliff(wx, wz);
                OverhangAmp[c] = Fields.TerrainField.OverhangAmplitude(Ruggedness[c], Cliff[c]);
                SoilDepth[c] = generator.Terrain.SoilDepth(wx, wz);
            }
    }

    public WorldGenerator Gen { get; }
    public Plan.WorldPlan Plan => Gen.Plan;
    public Fields.BlockPalette Palette => Gen.Palette;
    public Int3 Coord { get; }
    public Int3 ChunkOrigin { get; }

    /// <summary>World cell of padded index (0,0,0).</summary>
    public Int3 Origin { get; }

    public ushort[] Blocks { get; }
    public byte[]? Liquids { get; private set; }
    public byte[]? Shapes { get; private set; }
    public byte[]? Flags { get; private set; }

    /// <summary>Surface height per padded column (index = lz * Size + lx).</summary>
    public float[] SurfaceHeight { get; }
    public float[] Ruggedness { get; }

    /// <summary>Cliff-zone strength per padded column (0–1).</summary>
    public float[] Cliff { get; }

    /// <summary>
    /// 3D overhang amplitude per padded column (cells): the terrain is solid everywhere below SurfaceHeight − OverhangAmp
    /// (the true surface can dip that far), and open above SurfaceHeight + OverhangAmp.
    /// </summary>
    public float[] OverhangAmp { get; }
    public float[] SoilDepth { get; }

    /// <summary>Biome runtime id per padded cell (filled by the biome pass).</summary>
    public byte[] Biome { get; }

    public List<TileObjectInstance> Objects { get; } = new();

    /// <summary>Filled by the navigation pass.</summary>
    public ChunkNavSummary Nav { get; set; }

    /// <summary>True when no cell of the padded box is solid (lets passes skip work).</summary>
    public bool AllAir { get; set; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Index(int lx, int ly, int lz) => (ly * Size + lz) * Size + lx;

    public static int Column(int lx, int lz) => lz * Size + lx;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Contains(int wx, int wy, int wz) =>
        (uint)(wx - Origin.X) < Size && (uint)(wy - Origin.Y) < Size && (uint)(wz - Origin.Z) < Size;

    public bool InCore(int wx, int wy, int wz) =>
        (uint)(wx - ChunkOrigin.X) < ChunkMath.Size && (uint)(wy - ChunkOrigin.Y) < ChunkMath.Size && (uint)(wz - ChunkOrigin.Z) < ChunkMath.Size;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int WorldIndex(int wx, int wy, int wz) => Index(wx - Origin.X, wy - Origin.Y, wz - Origin.Z);

    public ushort Get(int wx, int wy, int wz) => Contains(wx, wy, wz) ? Blocks[WorldIndex(wx, wy, wz)] : Palette.Air;

    public void Set(int wx, int wy, int wz, ushort block)
    {
        if (Contains(wx, wy, wz)) Blocks[WorldIndex(wx, wy, wz)] = block;
    }

    public byte[] EnsureLiquids() => Liquids ??= new byte[Volume];
    public byte[] EnsureShapes() => Shapes ??= new byte[Volume];
    public byte[] EnsureFlags() => Flags ??= new byte[Volume];

    /// <summary>Whether a padded cell was written by a protected structure (the Dungeon's walls and halls, a temple):
    /// later passes leave such cells alone.</summary>
    public bool IsStructure(int index) => Flags != null && (Flags[index] & (byte)Voxels.CellFlags.Structure) != 0;

    public bool IsStructure(int wx, int wy, int wz) => Contains(wx, wy, wz) && IsStructure(WorldIndex(wx, wy, wz));

    public void SetLiquid(int wx, int wy, int wz, byte liquid)
    {
        if (Contains(wx, wy, wz)) EnsureLiquids()[WorldIndex(wx, wy, wz)] = liquid;
    }

    /// <summary>Deterministic random stream for a pass in this chunk.</summary>
    public Pcg32 Rng(GenPass pass) => Plan.Seeds.Child(pass.Name).Child(Coord.X, Coord.Y, Coord.Z).Rng();

    /// <summary>Deterministic per-cell hash for a pass (0–1).</summary>
    public float CellRandom(GenPass pass, int wx, int wy, int wz) =>
        Hash.ToUnitFloat(Hash.Cell(Plan.Seeds.Child(pass.Name).Value, wx, wy, wz));

    /// <summary>Normalized world depth of a y level.</summary>
    public float Depth(int wy) => Plan.Bands.NormalizedDepth(wy);

    /// <summary>
    /// Adds a tile object owned by this chunk (its anchor must be in the core). Tall objects such as trees
    /// may extend into neighbouring chunks.
    /// </summary>
    public bool AddObject(TileObjectInstance obj)
    {
        var b = obj.Bounds;
        if (!InCore(b.Min.X, b.Min.Y, b.Min.Z)) return false;
        foreach (var o in Objects)
            if (o.Bounds.Intersects(b)) return false;
        Objects.Add(obj);
        return true;
    }
}
