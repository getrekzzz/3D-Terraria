using Strata.Core.Determinism;

namespace Strata.Core.WorldGen.Plan;

/// <summary>How a biome region extends vertically.</summary>
public enum RegionProfile
{
    /// <summary>Surface biome whose column reaches down to the underworld (tundra, jungle).</summary>
    DeepColumn,

    /// <summary>Surface biome that tapers out partway down (evil biomes, surface desert).</summary>
    ShallowColumn,

    /// <summary>Buried 3D ellipsoid (underground desert, mushroom hollows).</summary>
    Buried,
}

/// <summary>A placed biome region: an irregular, noise-warped blob in XZ with a vertical profile.</summary>
public sealed class BiomeRegion
{
    public required string Biome { get; init; }

    /// <summary>Runtime id of <see cref="Biome"/>.</summary>
    public required byte BiomeId { get; init; }
    public required int Priority { get; init; }
    public required RegionProfile Profile { get; init; }
    public required float CenterX { get; init; }
    public required float CenterZ { get; init; }
    public required float RadiusX { get; init; }
    public required float RadiusZ { get; init; }

    private readonly float _angle;
    private readonly float _cos = 1f, _sin;

    /// <summary>Rotation of the ellipse in turns.</summary>
    public float Angle
    {
        get => _angle;
        init
        {
            _angle = value;
            _cos = Trig.Cos(value);
            _sin = Trig.Sin(value);
        }
    }

    /// <summary>For shallow columns: normalized depth where the region ends. For buried: centre depth.</summary>
    public float DepthCenter { get; init; } = 1f;

    /// <summary>For buried regions: vertical radius in normalized depth.</summary>
    public float DepthRadius { get; init; } = 0.3f;

    public required NoiseField Edge { get; init; }

    /// <summary>
    /// Membership strength: &gt; 0 inside (up to ~1 at the centre), &lt;= 0 outside. <paramref name="depth"/>
    /// is normalized depth (0 = sea level, 1 = underworld top).
    /// </summary>
    public float Membership(float x, float z, float depth)
    {
        var terms = ColumnTerms(x, z);
        return terms.Distance < 0f ? -1f : MembershipAt(terms, depth);
    }

    /// <summary>Depth-independent part of membership for a column; Distance &lt; 0 means "far outside".</summary>
    public (float Distance, float Warp) ColumnTerms(float x, float z)
    {
        float dx = x - CenterX, dz = z - CenterZ;
        float u = (dx * _cos + dz * _sin) / RadiusX;
        float v = (-dx * _sin + dz * _cos) / RadiusZ;
        float d2 = u * u + v * v;
        if (d2 > 4.5f) return (-1f, 0f);
        return (MathF.Sqrt(d2), Edge.Sample(x, z) * 0.28f);
    }


    public float MembershipAt((float Distance, float Warp) terms, float depth)
    {
        float scale = VerticalScale(depth);
        if (scale <= 0f || terms.Distance < 0f) return -1f;
        return 1f - terms.Distance / scale + terms.Warp;
    }

    public float VerticalScale(float depth)
    {
        switch (Profile)
        {
            case RegionProfile.DeepColumn:
                if (depth > 1.08f) return 0f;
                return depth < 0f ? 1f : 1f + 0.18f * depth * (1f - depth) * 4f;
            case RegionProfile.ShallowColumn:
                if (depth > DepthCenter) return 0f;
                return depth <= 0f ? 1f : 1f - 0.5f * (depth / DepthCenter) * (depth / DepthCenter);
            default:
                float t = (depth - DepthCenter) / DepthRadius;
                float r = 1f - t * t;
                return r <= 0f ? 0f : MathF.Sqrt(r);
        }
    }

    public bool ContainsXZ(float x, float z, float margin = 1.3f)
    {
        float r = MathF.Max(RadiusX, RadiusZ) * margin * 1.2f;
        float dx = x - CenterX, dz = z - CenterZ;
        return dx * dx + dz * dz <= r * r;
    }
}
