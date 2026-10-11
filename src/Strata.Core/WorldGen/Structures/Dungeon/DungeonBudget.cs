using Strata.Core.Common;
using Strata.Core.World;
using Strata.Core.WorldGen.Plan;

namespace Strata.Core.WorldGen.Structures.Dungeon;

/// <summary>Where the dungeon goes. <see cref="Gate"/> is the ground cell (top solid cell) at the keep's centre;
/// <see cref="Inward"/> is the horizontal unit axis toward the world centre. <see cref="Surface"/> returns the ground
/// height (top solid cell) of a column and <see cref="Landness"/> its land value in [0, 1] (1 = inland).</summary>
public sealed class DungeonSite
{
    public required Int3 Gate { get; init; }
    public required Int3 Inward { get; init; }
    public required Int3 WorldSize { get; init; }
    public required LayerBands Bands { get; init; }
    public required Func<int, int, int> Surface { get; init; }
    public required Func<int, int, float> Landness { get; init; }
}

/// <summary>How much dungeon a world of a given size gets.</summary>
/// <param name="HallCells">Trunk hall length (cells travelled) to aim for.</param>
/// <param name="MinRooms">Rooms with a role (not junctions, alcoves or the keep) to reach.</param>
/// <param name="LockedChests">Gold-chest spots to reserve.</param>
/// <param name="Branches">Side branches off the trunk.</param>
/// <param name="FloorBelowCavernTop">How far below the cavern top the deepest floor may go.</param>
public sealed record DungeonBudget(int HallCells, int MinRooms, int LockedChests, int Branches, int FloorBelowCavernTop)
{
    public static DungeonBudget For(WorldSizePreset size) => size switch
    {
        WorldSizePreset.Tiny => new DungeonBudget(260, 6, 4, 2, 10),
        WorldSizePreset.Small => new DungeonBudget(700, 14, 8, 5, 40),
        WorldSizePreset.Medium => new DungeonBudget(1200, 22, 12, 8, 60),
        _ => new DungeonBudget(1800, 30, 16, 11, 80),
    };

    /// <summary>Lowest interior floor: CavernTop - FloorBelowCavernTop, never below UnderworldTop + 40. A Don't Dig Up
    /// world's dungeon stands on the bottom surface and descends into the ground under it, down to just above the bedrock.</summary>
    public int FloorY(LayerBands bands) =>
        bands.Inverted ? InvertedFloor : Math.Max(bands.CavernTop - FloorBelowCavernTop, bands.UnderworldTop + 40);

    /// <summary>Lowest interior floor in a Don't Dig Up world.</summary>
    public const int InvertedFloor = 14;
}
