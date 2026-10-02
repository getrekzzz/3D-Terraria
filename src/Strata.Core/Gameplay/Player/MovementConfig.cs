using System.Text.Json;
using Strata.Core.Content;
using Strata.Core.Content.Loading;

namespace Strata.Core.Gameplay.Player;

/// <summary>Player movement tuning (data/config/movement.json). Distances in meters, times in seconds.</summary>
public sealed class MovementConfig
{
    public float WalkSpeed { get; init; } = 5.0f;
    /// <summary>Multiplier on the run-speed bonus while Sprint is held; without speed boots Sprint does nothing.</summary>
    public float SprintMultiplier { get; init; } = 1.35f;

    /// <summary>Seconds of running at full walking speed before speed boots reach their top speed (the run-up).</summary>
    public float RunUpTime { get; init; } = 1.2f;
    public float GroundAcceleration { get; init; } = 40f;
    public float GroundFriction { get; init; } = 30f;
    public float AirAcceleration { get; init; } = 22f;
    public float Gravity { get; init; } = 32f;

    /// <summary>Gravity multiplier while the jump button is held during the rising part of a jump.</summary>
    public float JumpHoldGravity { get; init; } = 0.45f;
    public float JumpHoldTime { get; init; } = 0.28f;
    public float JumpSpeed { get; init; } = 9.9f;
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

    /// <summary>
    /// Top horizontal speed: walking speed with the general move bonus, plus the run-speed bonus of speed boots eased
    /// in over <see cref="RunUpTime"/> (smoothstep). Sprint multiplies only that run bonus.
    /// </summary>
    public float TopSpeed(float moveBonus, float runBonus, float runUpSeconds, bool sprint)
    {
        float walk = WalkSpeed * (1f + moveBonus);
        if (runBonus <= 0) return walk;
        float t = Math.Clamp(runUpSeconds / RunUpTime, 0f, 1f);
        return walk * (1f + runBonus * (sprint ? SprintMultiplier : 1f) * t * t * (3f - 2f * t));
    }

    /// <summary>
    /// Advances the run-up timer: it builds while running on the ground near walking speed, holds through the air and
    /// resets when the player stops or slows down.
    /// </summary>
    public float AdvanceRunUp(float runUp, float dt, bool onFloor, bool moving, float speed, float walkSpeed)
    {
        if (!moving || speed < walkSpeed * 0.5f) return 0f;
        if (!onFloor) return runUp;
        return speed >= walkSpeed * 0.9f ? MathF.Min(runUp + dt, RunUpTime) : runUp;
    }

    /// <summary>Apex (m) of a ground jump held for the whole hold time: the closed form of the controller's integration.</summary>
    public float JumpApex(float jumpScale = 1f)
    {
        float v0 = JumpSpeed * MathF.Sqrt(jumpScale);
        float held = Gravity * JumpHoldGravity;
        float t1 = MathF.Min(JumpHoldTime, v0 / held);
        float v1 = v0 - held * t1;
        return (v0 + v1) * 0.5f * t1 + v1 * v1 / (2f * Gravity);
    }

    /// <summary>Fall damage for a fall of <paramref name="fallMeters"/> (0 when safe or negated).</summary>
    public int FallDamage(float fallMeters, bool noFallDamage)
    {
        if (noFallDamage) return 0;
        float cells = fallMeters / Common.Units.CellSize - SafeFallCells;
        return cells <= 0 ? 0 : (int)MathF.Round(cells * FallDamagePerCell);
    }
}
