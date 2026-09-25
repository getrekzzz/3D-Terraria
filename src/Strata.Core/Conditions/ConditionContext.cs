using Strata.Core.Determinism;
using Strata.Core.World;

namespace Strata.Core.Conditions;

/// <summary>
/// Everything a data-driven condition may inspect. Reused between evaluations to avoid allocation;
/// callers fill only the fields relevant to their domain (spawning, crafting, shops, events…).
/// </summary>
public sealed class ConditionContext
{
    private static readonly HashSet<string> Empty = new();

    /// <summary>Biome ids applying at the position (any order).</summary>
    public IReadOnlyCollection<string> Biomes { get; set; } = Array.Empty<string>();

    public WorldLayer Layer { get; set; } = WorldLayer.Surface;

    /// <summary>Clock time in hours [0, 24).</summary>
    public float Hour { get; set; } = 12f;

    public bool IsDay { get; set; } = true;

    /// <summary>0 = full moon … 7.</summary>
    public int MoonPhase { get; set; }

    /// <summary>Cells below the local surface (negative when above ground).</summary>
    public float Depth { get; set; }

    /// <summary>Effective light level 0–15 at the position.</summary>
    public int Light { get; set; } = 15;

    public IReadOnlySet<string> Flags { get; set; } = Empty;
    public IReadOnlySet<string> Events { get; set; } = Empty;
    public IReadOnlySet<string> NearbyLiquids { get; set; } = Empty;

    public Pcg32 Random;

    public bool HasBiome(string id)
    {
        foreach (var b in Biomes)
            if (b == id) return true;
        return false;
    }
}
