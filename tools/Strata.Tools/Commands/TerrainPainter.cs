using Strata.Core.Diagnostics;
using Strata.Core.WorldGen;
using Strata.Core.WorldGen.Analysis;

namespace Strata.Tools.Commands;

/// <summary>
/// Renders a <see cref="SurfaceProfile"/> at one pixel per column (north up): a hillshade of the Top heightfield lit
/// from the north-west and tinted by surface biome, and a flag map (grey hillshade with lips, floating cells,
/// breaches and water marked) that shows where the terrain overhangs or breaks open.
/// </summary>
internal sealed class TerrainPainter
{
    private static readonly float Sqrt2 = MathF.Sqrt(2f);

    private readonly WorldGenerator _gen;
    private readonly SurfaceProfile _profile;
    private readonly float[] _shade;

    public TerrainPainter(WorldGenerator gen, SurfaceProfile profile)
    {
        _gen = gen;
        _profile = profile;
        _shade = new float[profile.ColumnCount];
        for (int z = 0; z < profile.Depth; z++)
            for (int x = 0; x < profile.Width; x++)
                _shade[z * profile.Width + x] = Shade(x, z);
    }

    /// <summary>terrain_hillshade.png: relief lit from the north-west, coloured by the biome map colour.</summary>
    public void Hillshade(string path)
    {
        var p = _profile;
        var biomeColors = _gen.Content.Biomes.All.Select(b => ReportPainter.Parse(b.MapColor)).ToArray();
        var rgba = NewImage();
        for (int i = 0; i < p.ColumnCount; i++)
        {
            var c = p.Biome != null ? biomeColors[p.Biome[i]] : ((byte)110, (byte)150, (byte)90);
            float light = Math.Clamp(0.25f + 0.75f * _shade[i], 0.1f, 1.35f);
            float r = c.Item1 * light, g = c.Item2 * light, b = c.Item3 * light;
            if ((p.Flags[i] & SurfaceFlags.Water) != 0) (r, g, b) = (r * 0.4f + 24f, g * 0.4f + 54f, b * 0.4f + 120f);
            Set(rgba, i, r, g, b);
        }
        MarkSpawn(rgba);
        PngWriter.Write(path, p.Width, p.Depth, rgba);
    }

    /// <summary>terrain_flags.png: grey hillshade; red lips, yellow floating cells, blue breaches, cyan water.</summary>
    public void FlagMap(string path)
    {
        var p = _profile;
        var rgba = NewImage();
        for (int i = 0; i < p.ColumnCount; i++)
        {
            var f = p.Flags[i];
            float grey = Math.Clamp(40f + 120f * _shade[i], 20f, 200f);
            var (r, g, b) =
                (f & SurfaceFlags.Lip) != 0 ? (235f, 40f, 40f) :
                (f & SurfaceFlags.Floating) != 0 ? (245f, 215f, 40f) :
                (f & SurfaceFlags.Breach) != 0 ? (50f, 90f, 235f) :
                (f & SurfaceFlags.Water) != 0 ? (40f, 200f, 215f) :
                (grey, grey, grey);
            Set(rgba, i, r, g, b);
        }
        MarkSpawn(rgba);
        PngWriter.Write(path, p.Width, p.Depth, rgba);
    }

    /// <summary>Lambert term of the Top-gradient normal against a light 45° above the north-west, 1 on flat ground.</summary>
    private float Shade(int x, int z)
    {
        var p = _profile;
        int xl = Math.Max(0, x - 1), xr = Math.Min(p.Width - 1, x + 1);
        int zu = Math.Max(0, z - 1), zd = Math.Min(p.Depth - 1, z + 1);
        float gx = (p.Top[z * p.Width + xr] - p.Top[z * p.Width + xl]) / (float)Math.Max(1, xr - xl);
        float gz = (p.Top[zd * p.Width + x] - p.Top[zu * p.Width + x]) / (float)Math.Max(1, zd - zu);
        // Normal (-gx, 1, -gz), light towards (-1, √2, -1)/2 (north = -z, west = -x).
        float lit = (gx + gz + Sqrt2) / (2f * MathF.Sqrt(gx * gx + gz * gz + 1f));
        return MathF.Max(0f, lit) / (Sqrt2 / 2f);
    }

    private void MarkSpawn(byte[] rgba)
    {
        var s = _gen.Plan.Spawn;
        for (int dz = -2; dz <= 2; dz++)
            for (int dx = -2; dx <= 2; dx++)
            {
                int x = s.X + dx, z = s.Z + dz;
                if (dx * dx + dz * dz <= 4 && _profile.Contains(x, z)) Set(rgba, _profile.Index(x, z), 255, 255, 255);
            }
    }

    private byte[] NewImage()
    {
        var rgba = new byte[_profile.ColumnCount * 4];
        for (int i = 3; i < rgba.Length; i += 4) rgba[i] = 255;
        return rgba;
    }

    private static void Set(byte[] rgba, int i, float r, float g, float b)
    {
        rgba[i * 4] = (byte)Math.Clamp(r, 0, 255);
        rgba[i * 4 + 1] = (byte)Math.Clamp(g, 0, 255);
        rgba[i * 4 + 2] = (byte)Math.Clamp(b, 0, 255);
    }
}
