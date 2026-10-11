using Strata.Core.Content;
using Strata.Core.Content.Defs;
using Strata.Core.Presentation;

namespace Strata.Core.Meshing;

/// <summary>How a cell participates in meshing (precomputed per block runtime id).</summary>
public enum CellClass : byte
{
    /// <summary>Air or non-rendered.</summary>
    Empty,

    /// <summary>Natural opaque material in the smooth surface.</summary>
    Smooth,

    /// <summary>Built opaque full cube (crisp faces, snaps the surface to the grid).</summary>
    Crisp,

    /// <summary>See-through full cube (glass, leaves, webs, crystals).</summary>
    Transparent,
}

/// <summary>Texture layers of one render surface (a distinct top/side/bottom combination).</summary>
public readonly record struct SurfaceLayers(int Top, int Side, int Bottom);

/// <summary>
/// Per-block rendering and light properties resolved once from content + the texture atlas layout.
/// Immutable and shared by all mesh jobs.
/// </summary>
public sealed class RenderTables
{
    public RenderTables(ContentDatabase content, Func<string, int> layerIndex, GrassConfig? grass = null)
    {
        PaintColors = new System.Numerics.Vector3[Voxels.CellPaint.Max + 1];
        foreach (var item in content.Items.All)
            if (item.Paint is > 0 and <= Voxels.CellPaint.Max && item.PaintColor is { Length: 7 } hex)
                PaintColors[item.Paint] = new System.Numerics.Vector3(
                    Convert.ToInt32(hex.Substring(1, 2), 16) / 255f, Convert.ToInt32(hex.Substring(3, 2), 16) / 255f, Convert.ToInt32(hex.Substring(5, 2), 16) / 255f);
        int n = content.Blocks.Count;
        Class = new CellClass[n];
        Surface = new ushort[n];
        Solid = new bool[n];
        LightFilter = new byte[n];
        Emission = new ushort[n];
        LiquidEmission = new ushort[LiquidDef.MaxCode + 1];
        foreach (var liquid in content.Liquids.All)
            if (liquid.Light is { Length: 3 } ll && liquid.Code is >= 1 and <= LiquidDef.MaxCode)
                LiquidEmission[liquid.Code] = LightValue.Pack(0, ll[0], ll[1], ll[2]);
        Cutout = new bool[n];
        var surfaces = new List<SurfaceLayers>();
        // Blocks share a surface when they look alike: same texture layers and the same grass.
        var bySurface = new Dictionary<(SurfaceLayers, int, float), ushort>();
        var grassKinds = new List<int>();
        var glows = new List<float>();
        var kindIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        if (grass != null)
            for (int k = 0; k < grass.Ordered.Count; k++) kindIndex[grass.Ordered[k].Key] = k;
        foreach (var b in content.Blocks.All)
        {
            int id = b.RuntimeId;
            Class[id] = b.Render switch
            {
                BlockRender.Smooth => CellClass.Smooth,
                BlockRender.Crisp => CellClass.Crisp,
                BlockRender.Transparent => CellClass.Transparent,
                _ => CellClass.Empty,
            };
            Solid[id] = b.IsSolid;
            Cutout[id] = b.HasTag("cutout");
            LightFilter[id] = b.IsOpaque ? (byte)15 : b.HasTag("leaf") ? (byte)2 : b.Render == BlockRender.Transparent ? (byte)1 : (byte)0;
            if (b.Light is { Length: 3 } l) Emission[id] = LightValue.Pack(0, l[0], l[1], l[2]);
            if (b.Render == BlockRender.None) continue;
            var layers = new SurfaceLayers(layerIndex(b.Textures.TopLayer), layerIndex(b.Textures.SideLayer), layerIndex(b.Textures.BottomLayer));
            int kind = kindIndex.TryGetValue(b.Id, out int k) ? k : -1;
            float glow = b.Glow ?? 0f;
            if (!bySurface.TryGetValue((layers, kind, glow), out var s))
            {
                s = (ushort)surfaces.Count;
                surfaces.Add(layers);
                grassKinds.Add(kind);
                glows.Add(glow);
                bySurface[(layers, kind, glow)] = s;
            }
            Surface[id] = s;
        }
        Surfaces = surfaces;
        GrassKind = grassKinds.ToArray();
        SurfaceGlow = glows.ToArray();
        Grass = grass;
    }

    /// <summary>Colour of each paint index (index 0 unused).</summary>
    public System.Numerics.Vector3[] PaintColors { get; }

    public CellClass[] Class { get; }

    /// <summary>Render surface id per block (index into <see cref="Surfaces"/>).</summary>
    public ushort[] Surface { get; }

    public bool[] Solid { get; }

    /// <summary>Alpha-tested see-through blocks (leaves, webs) rendered in the cutout pass rather than blended.</summary>
    public bool[] Cutout { get; }

    /// <summary>Extra light attenuation per step through the block (15 = opaque).</summary>
    public byte[] LightFilter { get; }

    /// <summary>Emitted light per block (packed <see cref="LightValue"/>).</summary>
    public ushort[] Emission { get; }

    /// <summary>Packed block light emitted per liquid code (lava glows).</summary>
    public ushort[] LiquidEmission { get; }

    public IReadOnlyList<SurfaceLayers> Surfaces { get; }

    /// <summary>Shader-only self-glow per render surface (<see cref="BlockDef.Glow"/>, 0 for none).</summary>
    public float[] SurfaceGlow { get; }

    /// <summary>Grass kind per render surface (index into <see cref="GrassConfig.Ordered"/>), -1 for none.</summary>
    public int[] GrassKind { get; }

    /// <summary>Grass settings; null when the tables were built without grass (tools, tests).</summary>
    public GrassConfig? Grass { get; }

    public bool IsOpaque(ushort block) => Class[block] is CellClass.Smooth or CellClass.Crisp;
}
