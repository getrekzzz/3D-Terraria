using System.Text.Json;
using Strata.Core.Content;
using Strata.Core.Content.Loading;

namespace Strata.Core.Gameplay.Player;

/// <summary>Player movement tuning (data/config/movement.json). Distances in meters, times in seconds.</summary>
public sealed class MovementConfig
{
    public float WalkSpeed { get; init; } = 5.0f;
    public float SprintMultiplier { get; init; } = 1.35f;
    public float GroundAcceleration { get; init; } = 40f;
    public float GroundFriction { get; init; } = 30f;
    public float AirAcceleration { get; init; } = 22f;
    public float Gravity { get; init; } = 32f;

    /// <summary>Gravity multiplier while the jump button is held during the rising part of a jump.</summary>
    public float JumpHoldGravity { get; init; } = 0.45f;
    public float JumpHoldTime { get; init; } = 0.28f;
    public float JumpSpeed { get; init; } = 8.2f;
    public float CoyoteTime { get; init; } = 0.12f;
    public float JumpBuffer { get; init; } = 0.12f;
    public float MaxFallSpeed { get; init; } = 30f;

    /// <summary>Highest ledge climbed automatically while walking (one cell plus margin).</summary>
    public float StepHeight { get; init; } = 0.55f;

    public float SwimGravity { get; init; } = 6f;
    public float SwimSpeed { get; init; } = 3.2f;
    public float SwimUpSpeed { get; init; } = 3.6f;

    /// <summary>Cells fallen before damage starts (the reference game uses 25 tiles).</summary>
    public float SafeFallCells { get; init; } = 25f;
    public float FallDamagePerCell { get; init; } = 10f;

    public static MovementConfig Load(IDataSource data)
    {
        const string path = "data/config/movement.json";
        if (!data.Exists(path)) throw new FileNotFoundException("Missing movement config", path);
        return JsonSerializer.Deserialize<MovementConfig>(data.ReadText(path), ContentJson.Options)
            ?? throw new JsonException("movement.json is empty");
    }

    /// <summary>Fall damage for a fall of <paramref name="fallMeters"/> (0 when safe or negated).</summary>
    public int FallDamage(float fallMeters, bool noFallDamage)
    {
        if (noFallDamage) return 0;
        float cells = fallMeters / Common.Units.CellSize - SafeFallCells;
        return cells <= 0 ? 0 : (int)MathF.Round(cells * FallDamagePerCell);
    }
}
