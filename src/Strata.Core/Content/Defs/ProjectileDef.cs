namespace Strata.Core.Content.Defs;

/// <summary>A projectile's physical and visual behaviour.</summary>
public sealed class ProjectileDef : ContentDef
{
    public string? Model { get; init; }

    /// <summary>Visual style used when no model is given (arrow, bolt, orb, spark, bullet).</summary>
    public string Visual { get; init; } = "orb";

    public string Color { get; init; } = "#ffffff";

    /// <summary>Gravity in m/s² (0 for magic bolts).</summary>
    public float Gravity { get; init; }

    public float Drag { get; init; }

    public int Lifetime { get; init; } = 180;

    /// <summary>Collision radius in cells.</summary>
    public float Radius { get; init; } = 0.4f;

    /// <summary>Number of enemies it can pass through (-1 = infinite).</summary>
    public int Pierce { get; init; } = 1;

    public int Bounces { get; init; }
    public bool CollideTerrain { get; init; } = true;
    public bool Friendly { get; init; } = true;
    public bool Hostile { get; init; }

    public int[]? Light { get; init; }
    public string? Trail { get; init; }

    public TypedSpec[] OnHit { get; init; } = Array.Empty<TypedSpec>();
}
