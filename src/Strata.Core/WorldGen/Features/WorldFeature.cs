using System.Numerics;
using Strata.Core.Common;
using Strata.Core.Voxels;

namespace Strata.Core.WorldGen.Features;

/// <summary>A plan-level feature occupying a bounded region. Chunk passes query them via <see cref="FeatureIndex"/>.</summary>
public abstract class WorldFeature
{
    public required string Kind { get; init; }

    public Aabb3i Bounds { get; protected init; }

    /// <summary>Biome/zone id this feature represents for gameplay (spawns, music), if any.</summary>
    public string? Zone { get; init; }

    public ulong Seed { get; init; }

    /// <summary>Does this cell belong to the feature's <see cref="Zone"/>? (Default: inside the bounds.)</summary>
    public virtual bool InZone(Int3 cell) => Bounds.Contains(cell);
}

/// <summary>Capsule segment carving tunnels, shafts, rivers, lava tubes and chasms.</summary>
public sealed class CapsuleFeature : WorldFeature
{
    public CapsuleFeature(Vector3 a, Vector3 b, float radiusA, float radiusB, float verticalScale = 1f)
    {
        A = a;
        B = b;
        RadiusA = radiusA;
        RadiusB = radiusB;
        VerticalScale = verticalScale;
        float r = MathF.Max(radiusA, radiusB) + 1;
        var min = Vector3.Min(a, b) - new Vector3(r);
        var max = Vector3.Max(a, b) + new Vector3(r);
        Bounds = new Aabb3i(new Int3((int)MathF.Floor(min.X), (int)MathF.Floor(min.Y), (int)MathF.Floor(min.Z)),
            new Int3((int)MathF.Ceiling(max.X) + 1, (int)MathF.Ceiling(max.Y) + 1, (int)MathF.Ceiling(max.Z) + 1));
    }

    public Vector3 A { get; }
    public Vector3 B { get; }
    public float RadiusA { get; }
    public float RadiusB { get; }

    /// <summary>&lt; 1 flattens the cross-section (wide, low river channels).</summary>
    public float VerticalScale { get; }

    /// <summary>Liquid poured into the lower part of the tube.</summary>
    public LiquidType Fill { get; init; }

    /// <summary>Fraction of the tube height filled with <see cref="Fill"/> (0–1).</summary>
    public float FillFraction { get; init; }

    /// <summary>Block lining the tube wall (obsidian crust on lava tubes, evil stone in chasms), or null.</summary>
    public string? Lining { get; init; }
}

public enum ChamberKind
{
    Cavern,
    Grand,
    Lake,
    Geode,
    Collapsed,
    Hive,
    SpiderNest,
    Marble,
    Granite,
    Mushroom,
    Shrine,
}

/// <summary>Ellipsoidal chamber with a kind-specific interior.</summary>
public sealed class ChamberFeature : WorldFeature
{
    public ChamberFeature(Vector3 center, Vector3 radii)
    {
        Center = center;
        Radii = radii;
        var min = center - radii - Vector3.One * 2;
        var max = center + radii + Vector3.One * 2;
        Bounds = new Aabb3i(new Int3((int)MathF.Floor(min.X), (int)MathF.Floor(min.Y), (int)MathF.Floor(min.Z)),
            new Int3((int)MathF.Ceiling(max.X) + 1, (int)MathF.Ceiling(max.Y) + 1, (int)MathF.Ceiling(max.Z) + 1));
    }

    public Vector3 Center { get; }
    public Vector3 Radii { get; }
    public required ChamberKind Chamber { get; init; }

    public override bool InZone(Int3 cell) => Distance(cell.X + 0.5f, cell.Y + 0.5f, cell.Z + 0.5f) <= 1.15f;

    /// <summary>Normalized ellipsoid distance: &lt; 1 inside.</summary>
    public float Distance(float x, float y, float z)
    {
        float dx = (x - Center.X) / Radii.X, dy = (y - Center.Y) / Radii.Y, dz = (z - Center.Z) / Radii.Z;
        return MathF.Sqrt(dx * dx + dy * dy + dz * dz);
    }
}

/// <summary>Funnel-shaped sinkhole from the surface into a chamber.</summary>
public sealed class ConeFeature : WorldFeature
{
    public ConeFeature(Vector3 top, float topRadius, float bottomRadius, float depth)
    {
        Top = top;
        TopRadius = topRadius;
        BottomRadius = bottomRadius;
        Depth = depth;
        int r = (int)MathF.Ceiling(MathF.Max(topRadius, bottomRadius)) + 2;
        Bounds = new Aabb3i(new Int3((int)top.X - r, (int)(top.Y - depth) - 2, (int)top.Z - r),
            new Int3((int)top.X + r + 1, (int)top.Y + 24, (int)top.Z + r + 1));
    }

    public Vector3 Top { get; }
    public float TopRadius { get; }
    public float BottomRadius { get; }
    public float Depth { get; }
}

/// <summary>A floating landmass in the sky layer.</summary>
public sealed class IslandFeature : WorldFeature
{
    public IslandFeature(float x, float z, float topY, float radius, float depth)
    {
        X = x;
        Z = z;
        TopY = topY;
        Radius = radius;
        Depth = depth;
        int r = (int)MathF.Ceiling(radius * 1.35f) + 2;
        Bounds = new Aabb3i(new Int3((int)x - r, (int)(topY - depth) - 2, (int)z - r), new Int3((int)x + r + 1, (int)topY + 12, (int)z + r + 1));
    }

    public float X { get; }
    public float Z { get; }
    public float TopY { get; }
    public float Radius { get; }
    public float Depth { get; }
    public bool CloudLake { get; init; }
}

/// <summary>Natural rock arch (additive terrain): a torus segment standing on the ground.</summary>
public sealed class ArchFeature : WorldFeature
{
    public ArchFeature(Vector3 center, float majorRadius, float minorRadius, float angleTurns)
    {
        Center = center;
        MajorRadius = majorRadius;
        MinorRadius = minorRadius;
        Angle = angleTurns;
        int r = (int)MathF.Ceiling(majorRadius + minorRadius) + 2;
        Bounds = new Aabb3i(new Int3((int)center.X - r, (int)center.Y - 4, (int)center.Z - r),
            new Int3((int)center.X + r + 1, (int)(center.Y + majorRadius + minorRadius) + 2, (int)center.Z + r + 1));
    }

    public Vector3 Center { get; }
    public float MajorRadius { get; }
    public float MinorRadius { get; }
    public float Angle { get; }
}
