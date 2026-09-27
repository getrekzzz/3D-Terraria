using Strata.Core.Content;
using Strata.Core.WorldGen.Plan;

namespace Strata.Core.WorldGen.Fields;

/// <summary>Pure biome lookup for any cell (used by generation and by gameplay: spawns, music, fog).</summary>
public sealed class BiomeField
{
    private readonly WorldPlan _plan;
    private readonly TerrainField _terrain;

    public BiomeField(WorldPlan plan, TerrainField terrain, ContentDatabase content)
    {
        _plan = plan;
        _terrain = terrain;
        Forest = (byte)content.Biomes.Get("forest").RuntimeId;
        Ocean = (byte)content.Biomes.Get("ocean").RuntimeId;
        Sky = (byte)content.Biomes.Get("sky").RuntimeId;
        Underworld = (byte)content.Biomes.Get("underworld").RuntimeId;
        Content = content;
    }

    public ContentDatabase Content { get; }
    public byte Forest { get; }
    public byte Ocean { get; }
    public byte Sky { get; }
    public byte Underworld { get; }

    public string IdOf(byte biome) => Content.Biomes[biome].Id;

    /// <summary>Dominant biome at a cell.</summary>
    public byte BiomeAt(float x, float y, float z) => BiomeAt(x, y, z, _terrain.SurfaceHeight(x, z));

    public byte BiomeAt(float x, float y, float z, float surfaceHeight)
    {
        var bands = _plan.Bands;
        if (y < bands.UnderworldTop) return Underworld;
        if (y >= bands.SkyStart && y > surfaceHeight + 16f) return Sky;
        float depth = bands.NormalizedDepth(MathF.Min(y, surfaceHeight));
        var regions = _plan.Regions;
        for (int i = 0; i < regions.Count; i++)
            if (regions[i].Membership(x, z, depth) > 0f) return regions[i].BiomeId;
        if (_plan.Landness.Sample(x, z) < 0.5f && y > bands.CavernTop) return Ocean;
        return Forest;
    }

    /// <summary>
    /// Evaluates the biome for several heights of one column, computing the expensive XZ terms once.
    /// </summary>
    public void BiomeColumn(float x, float z, float surfaceHeight, ReadOnlySpan<float> ys, Span<byte> result)
    {
        var bands = _plan.Bands;
        var regions = _plan.Regions;
        Span<(float, float)> terms = regions.Count <= 32 ? stackalloc (float, float)[regions.Count] : new (float, float)[regions.Count];
        for (int r = 0; r < regions.Count; r++) terms[r] = regions[r].ColumnTerms(x, z);
        bool ocean = _plan.Landness.Sample(x, z) < 0.5f;
        for (int k = 0; k < ys.Length; k++)
        {
            float y = ys[k];
            if (y < bands.UnderworldTop) { result[k] = Underworld; continue; }
            if (y >= bands.SkyStart && y > surfaceHeight + 16f) { result[k] = Sky; continue; }
            float depth = bands.NormalizedDepth(MathF.Min(y, surfaceHeight));
            byte biome = ocean && y > bands.CavernTop ? Ocean : Forest;
            for (int r = 0; r < regions.Count; r++)
            {
                if (terms[r].Item1 >= 0f && regions[r].MembershipAt(terms[r], depth) > 0f)
                {
                    biome = regions[r].BiomeId;
                    break;
                }
            }
            result[k] = biome;
        }
    }

    /// <summary>Surface biome of a column (as seen standing on the ground).</summary>
    public byte SurfaceBiome(float x, float z)
    {
        float h = _terrain.SurfaceHeight(x, z);
        return BiomeAt(x, h, z, h);
    }
}
