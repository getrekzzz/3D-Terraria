namespace Strata.Core.Common;

/// <summary>Global unit conventions (see docs/CONVENTIONS.md).</summary>
public static class Units
{
    /// <summary>Edge length of one voxel cell in meters.</summary>
    public const float CellSize = 0.5f;

    /// <summary>Cells per meter.</summary>
    public const float CellsPerMeter = 1f / CellSize;

    /// <summary>Fixed simulation rate.</summary>
    public const int TicksPerSecond = 60;

    public const float SecondsPerTick = 1f / TicksPerSecond;

    public static float CellsToMeters(float cells) => cells * CellSize;
    public static float MetersToCells(float meters) => meters * CellsPerMeter;
    public static float TicksToSeconds(int ticks) => ticks * SecondsPerTick;
    public static int SecondsToTicks(float seconds) => (int)MathF.Round(seconds * TicksPerSecond);
}
