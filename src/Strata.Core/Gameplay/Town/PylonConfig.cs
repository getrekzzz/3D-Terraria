namespace Strata.Core.Gameplay.Town;

/// <summary>Terraria 1.4 pylon rules (data/config/town.json "pylons"); distances in metres.</summary>
public sealed class PylonConfig
{
    /// <summary>Price in copper before happiness scaling (Terraria: 10 gold).</summary>
    public long Price { get; init; } = 100000;

    /// <summary>The seller must be at least this content (happiness price factor at most this).</summary>
    public float MaxFactor { get; init; } = 1.0f;

    /// <summary>The seller needs another resident within this radius of their home.</summary>
    public float NeighbourRadius { get; init; } = 12.5f;

    /// <summary>A pylon works with <see cref="MinResidents"/> residents within this radius.</summary>
    public float ActiveRadius { get; init; } = 40f;
    public int MinResidents { get; init; } = 2;

    /// <summary>The player must be this close to a working pylon to teleport.</summary>
    public float UseRadius { get; init; } = 30f;

    /// <summary>Checked in order, so specific biomes come before the forest (defaults: Terraria's pylons).</summary>
    public PylonRule[] Rules { get; init; } =
    {
        new() { Object = "hallow_pylon", Biome = "hallow" }, new() { Object = "mushroom_pylon", Biome = "mushroom" },
        new() { Object = "jungle_pylon", Biome = "jungle" }, new() { Object = "desert_pylon", Biome = "desert" },
        new() { Object = "snow_pylon", Biome = "tundra" }, new() { Object = "ocean_pylon", Biome = "ocean" },
        new() { Object = "forest_pylon", Biome = "forest" }, new() { Object = "cavern_pylon", Layer = "underground" },
    };
}

/// <summary>A pylon tile object and the home it is sold from: a biome there, or a layer (the Cavern Pylon underground).</summary>
public sealed class PylonRule
{
    public string Object { get; init; } = "";
    public string? Biome { get; init; }

    /// <summary>"underground": any depth below the surface.</summary>
    public string? Layer { get; init; }
}
