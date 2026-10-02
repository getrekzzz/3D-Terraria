using Strata.Core.Content;
using Strata.Core.Determinism;
using Strata.Core.Diagnostics;
using Strata.Core.WorldGen.Chunk;
using Strata.Core.WorldGen.Plan;

namespace Strata.Core.WorldGen;

/// <summary>
/// One named stage of world generation. A pass may contribute to the global plan (run once per world)
/// and/or transform each chunk (pure: depends only on the plan and cell position).
/// Its <see cref="Name"/> seeds its randomness, so pass order never shifts another pass's output.
/// </summary>
public abstract class GenPass
{
    /// <summary>Stable identifier (also the randomness namespace).</summary>
    public abstract string Name { get; }

    /// <summary>Position in the directive's 20-pass pipeline (documentation/ordering).</summary>
    public abstract int Number { get; }

    public virtual void Plan(PlanContext context)
    {
    }

    public virtual void Generate(ChunkGenContext context)
    {
    }

    public override string ToString() => $"#{Number:00} {Name}";
}

/// <summary>Services available to a pass while building the plan.</summary>
public sealed class PlanContext
{
    public PlanContext(WorldGenerator generator) => Gen = generator;

    public WorldGenerator Gen { get; }
    public WorldPlan Plan => Gen.Plan;
    public ContentDatabase Content => Gen.Content;
    public ILog Log => Gen.Log;
    public Fields.TerrainField Terrain => Gen.Terrain;
    public Fields.BiomeField Biomes => Gen.Biomes;

    public SeedTree SeedsFor(GenPass pass) => Plan.Seeds.Child(pass.Name);

    /// <summary>World-space land radius (cells) of the continent around the centre.</summary>
    public float LandRadius => Plan.LandRadius;

    public float CenterX => Plan.Size.X * 0.5f;
    public float CenterZ => Plan.Size.Z * 0.5f;

    /// <summary>Random point on land (landness above threshold), at least <paramref name="minFromSpawn"/> from the centre.</summary>
    public bool TryRandomLandPoint(ref Pcg32 rng, float minLandness, float minFromSpawn, out float x, out float z)
    {
        for (int attempt = 0; attempt < 64; attempt++)
        {
            x = rng.Range(0f, Plan.Size.X - 1);
            z = rng.Range(0f, Plan.Size.Z - 1);
            float dx = x - CenterX, dz = z - CenterZ;
            if (dx * dx + dz * dz < minFromSpawn * minFromSpawn) continue;
            if (Plan.Landness.Sample(x, z) >= minLandness) return true;
        }
        x = CenterX;
        z = CenterZ;
        return false;
    }

    /// <summary>Scales a count authored for a Small world to this world's land area.</summary>
    public int Scaled(float smallWorldCount)
    {
        float area = (float)Plan.Size.X * Plan.Size.Z / (2048f * 2048f);
        return Math.Max(0, (int)MathF.Round(smallWorldCount * area));
    }
}
