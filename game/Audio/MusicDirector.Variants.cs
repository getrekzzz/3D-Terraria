using Godot;
using Strata.Core.Content.Defs;
using Strata.Core.Gameplay.Weather;
using Strata.Core.Session;
using Strata.Core.World;

namespace Strata.Game.Audio;

/// <summary>
/// Situational variants of the soundtrack, as in the reference game: the rain theme over the forest in a downpour,
/// the town themes near a settled town, underground themes for the jungle, ice and evil biomes, and space high above.
/// Each falls back to the plain track when the variant has none.
/// </summary>
public sealed partial class MusicDirector
{
    /// <summary>Residents within this many metres of the player make the forest a town.</summary>
    public const float TownRadius = 40f;

    /// <summary>Residents needed nearby for the town theme.</summary>
    public const int TownResidents = 3;

    /// <summary>The variant of a biome's <paramref name="music"/> for the place, or null to use the usual track.</summary>
    private string? Variant(GameWorld game, PlayerEntity player, BiomeDef biome, string music, WorldLayer layer, LocalWeather weather, bool night)
    {
        if (layer is WorldLayer.Underground or WorldLayer.Cavern)
            return Resolve("underground_" + music.Replace("_day", ""));
        if (layer != WorldLayer.Surface || biome.Id is not ("forest" or "living_tree")) return null;
        if (weather is LocalWeather.Rain or LocalWeather.Storm && Resolve("rain") is { } rain) return rain;
        if (InTown(game, player)) return Resolve(night ? "town_night" : "town_day");
        return null;
    }

    /// <summary>The layer track when no biome has music of its own (space has its own above the sky islands).</summary>
    private string? LayerVariant(WorldLayer layer) => layer == WorldLayer.Space ? Resolve("space") : null;

    private static bool InTown(GameWorld game, PlayerEntity player)
    {
        int near = 0;
        var residents = game.Town.Residents;
        for (int i = 0; i < residents.Count; i++)
        {
            var r = residents[i];
            if (r.Alive && System.Numerics.Vector3.DistanceSquared(r.Position, player.Position) < TownRadius * TownRadius && ++near >= TownResidents) return true;
        }
        return false;
    }
}
