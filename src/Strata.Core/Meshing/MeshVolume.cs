using System.Numerics;
using Strata.Core.Voxels;

namespace Strata.Core.Meshing;

/// <summary>
/// Per-sample classification of a <see cref="Neighborhood"/> used by all meshers: which samples are
/// opaque for the surface, which force grid snapping (crisp/shaped/transparent), and per-sample light.
/// </summary>
public sealed class MeshVolume
{
    public const int N = Neighborhood.Size;

    [ThreadStatic] private static MeshVolume? _pooled;

    public MeshVolume(Neighborhood nb, RenderTables tables, ushort[] light) => Reset(nb, tables, light);

    /// <summary>
    /// This thread's reusable volume, reset for a new neighbourhood (its three 256 KB arrays are allocated once per
    /// thread instead of once per mesh job). Valid until the next <see cref="Rent"/> on the same thread: meshers only
    /// read it during the build.
    /// </summary>
    public static MeshVolume Rent(Neighborhood nb, RenderTables tables, ushort[] light)
    {
        if (_pooled == null) return _pooled = new MeshVolume(nb, tables, light);
        _pooled.Reset(nb, tables, light);
        return _pooled;
    }

    private void Reset(Neighborhood nb, RenderTables tables, ushort[] light)
    {
        Nb = nb;
        Tables = tables;
        Light = light;
        var opaque = Opaque;
        var snap = Snap;
        var crisp = CrispCell;
        var cls = tables.Class;
        for (int i = 0; i < Neighborhood.Volume; i++)
        {
            ushort b = nb.Blocks[i];
            var c = cls[b];
            bool shaped = nb.HasShapes && nb.Shapes[i] != 0;
            // Natural materials placed by a player render as crisp blocks: what you build stays exact.
            bool placed = nb.HasFlags && (nb.Flags[i] & (byte)CellFlags.PlayerPlaced) != 0 && c == CellClass.Smooth;
            opaque[i] = (c == CellClass.Smooth || c == CellClass.Crisp) && !shaped;
            crisp[i] = opaque[i] && (c == CellClass.Crisp || placed);
            snap[i] = crisp[i] || c == CellClass.Transparent || shaped;
        }
    }

    public Neighborhood Nb { get; private set; } = null!;

    /// <summary>A cell's paint as surface-flag bits (paint index shifted past the crisp bit; 0 when unpainted).</summary>
    public int PaintBits(int index) => Nb.HasFlags ? (Voxels.CellPaint.Of(Nb.Flags[index]) << 1) : 0;
    public RenderTables Tables { get; private set; } = null!;
    public ushort[] Light { get; private set; } = null!;
    public bool[] Opaque { get; } = new bool[Neighborhood.Volume];
    public bool[] Snap { get; } = new bool[Neighborhood.Volume];

    /// <summary>Opaque cell rendered with crisp flat faces (built blocks, player-placed natural blocks).</summary>
    public bool[] CrispCell { get; } = new bool[Neighborhood.Volume];

    public static int Index(int x, int y, int z) => (y * N + z) * N + x;

    /// <summary>Converts region cell coordinates to chunk-local meters.</summary>
    public static Vector3 ToLocalMeters(Vector3 region) => (region - new Vector3(Neighborhood.Margin)) * Common.Units.CellSize;

    /// <summary>Average light of the non-opaque samples in the 2×2×2 block with min corner (x,y,z).</summary>
    public uint DualLight(int x, int y, int z)
    {
        float r = 0, g = 0, b = 0, s = 0;
        int n = 0;
        for (int k = 0; k < 8; k++)
        {
            int i = Index(x + (k & 1), y + ((k >> 1) & 1), z + (k >> 2));
            if (Opaque[i]) continue;
            ushort l = Light[i];
            r += LightValue.R(l);
            g += LightValue.G(l);
            b += LightValue.B(l);
            s += LightValue.Sky(l);
            n++;
        }
        if (n == 0) return 0;
        return MeshSurface.PackLight(r / n, g / n, b / n, s / n);
    }

    /// <summary>Ambient occlusion from the number of opaque samples in a dual cell (4 = flat).</summary>
    public float DualOcclusion(int x, int y, int z)
    {
        int count = 0;
        for (int k = 0; k < 8; k++)
            if (Opaque[Index(x + (k & 1), y + ((k >> 1) & 1), z + (k >> 2))]) count++;
        return count <= 4 ? 1f : 1f - (count - 4) * 0.09f;
    }

    public byte Liquid(int i) => Nb.Liquids[i];
}
