namespace Strata.Core.WorldGen.Caves;

/// <summary>
/// The cave network's algorithm constants (the generator's, not content). Changing any of them changes world output, so
/// it needs a <see cref="GenVersion"/> bump. Distances are cells (0.5 m); the player is 2 × 3.6 cells, steps up 1 cell,
/// jumps about 5 and falls 25 safely.
/// </summary>
public static class CaveTuning
{
    /// <summary>Network levels, top down: the underground, the upper caverns, the deep caverns, the abyss over the underworld.</summary>
    public const int Levels = 4;

    /// <summary>Room grid spacing per level (one candidate room per square of this size).</summary>
    public static readonly float[] Spacing = { 84f, 92f, 100f, 116f };

    /// <summary>Normalized depth band of each level's floors (underground rooms also keep <see cref="RoofRock"/> under the ground).</summary>
    public static readonly float[] DepthMin = { 0.10f, 0.36f, 0.57f, 0.80f };
    public static readonly float[] DepthMax = { 0.28f, 0.52f, 0.75f, 0.90f };

    /// <summary>Least rock between an underground room's ceiling and the surface above it.</summary>
    public const float RoofRock = 16f;

    /// <summary>Rooms keep this far inside land (landness at the centre and rim) so the sea never pours in.</summary>
    public const float MinLandness = 0.9f;

    /// <summary>No underground room nearer the spawn than this (horizontal cells).</summary>
    public const float SpawnClearance = 90f;

    /// <summary>Steepest floor a passage climbs or descends (rise over run, about 29°): walkable with one-cell steps.</summary>
    public const float MaxSlope = 0.56f;

    /// <summary>Switchback legs: length range, slope, and the widest angle a leg leaves the overall heading (turns).</summary>
    public const float LegMin = 26f, LegMax = 36f, LegSlope = 0.5f, LegSwing = 0.2f;

    /// <summary>Passage half-width and height ranges per level.</summary>
    public static readonly float[] PassageHalfWidthMin = { 3.2f, 3.5f, 3.8f, 3.8f };
    public static readonly float[] PassageHalfWidthMax = { 4.0f, 4.6f, 5.2f, 5.0f };
    public static readonly float[] PassageHeightMin = { 6.5f, 7f, 8f, 8f };
    public static readonly float[] PassageHeightMax = { 8f, 9.5f, 11f, 10.5f };

    /// <summary>Passage meander: one bend per this many cells, swinging at most this share of the length sideways.</summary>
    public const float BendEvery = 22f;
    public const float BendSwing = 0.09f;

    /// <summary>Each room links to up to this many nearest neighbours on its level (a spanning tree first, then loops).</summary>
    public const int Neighbours = 3;
    public const float LinkReach = 2.4f;
    public const float LoopChance = 0.3f;

    /// <summary>Share of rooms on a lower level that get a switchback descent from the nearest room above.</summary>
    public const float DescentChance = 0.22f;

    /// <summary>Share of underground rooms with a walk-in entrance from the surface, and its surface distance range.</summary>
    public const float EntranceChance = 0.22f;
    public const float EntranceMin = 34f, EntranceMax = 64f;

    /// <summary>Share of rooms with a dead-end spur to a treasure niche, and the spur's length range.</summary>
    public const float SpurChance = 0.35f;
    public const float SpurMin = 20f, SpurMax = 38f;

    /// <summary>
    /// Thickness (cells) of the rock shell round a room that the older noise caves may not cut, up to a share of the
    /// room's height by kind: whole for the enclosed rooms (domes, grottos, crystal caves, niches), the lower half or so
    /// for the great halls, so noise caves break in only high on their walls. Passages still open their doors.
    /// </summary>
    public const float ShellCells = 4f;

    public static float Enclosure(CaveRoomKind kind) => kind switch
    {
        CaveRoomKind.Dome or CaveRoomKind.Grotto or CaveRoomKind.Crystal or CaveRoomKind.Niche or CaveRoomKind.Alcove => 1f,
        CaveRoomKind.Lake or CaveRoomKind.Terraces or CaveRoomKind.Landing => 0.6f,
        CaveRoomKind.Pillars => 0.45f,
        _ => 0.5f,
    };

    /// <summary>Noise caves stay out of the rock under room floors (and passage floors and lower walls).</summary>
    public const int RoomFloorRock = 5;
    public const int PassageFloorRock = 3;
    public const float PassageWallBand = 0.3f;

    /// <summary>Wall and ceiling roughness: the share by which the warp noise moves a wall or the ceiling.</summary>
    public const float RoomWarp = 0.1f;
    public const float PassageWarp = 0.15f;
    public const float CeilingWarp = 0.1f;

    /// <summary>Wavelength (cells) of the warp noise.</summary>
    public const float WarpWavelength = 18f;
}
