using Strata.Core.Content;
using Strata.Core.Content.Specs;

namespace Strata.Core.Gameplay.Mounts;

/// <summary>
/// A rideable mount, the <c>mount</c> item behaviour (Slimy Saddle, Fuzzy Carrot, Honeyed Goggles, Cosmic Car Key…):
/// how it runs, jumps and flies, how it looks and what it does to what it lands on. Speeds are m/s; gravity scales
/// the player's (data/config/movement.json).
/// </summary>
public sealed class MountParams : SpecParams, IValidatableSpec
{
    /// <summary>The mount's id (its visual and buff).</summary>
    public string Id { get; init; } = "";

    public float RunSpeed { get; init; } = 7f;
    public float Acceleration { get; init; } = 30f;
    public float AirAcceleration { get; init; } = 18f;

    /// <summary>Take-off speed of a jump, and how long holding jump keeps gravity light.</summary>
    public float JumpSpeed { get; init; } = 10f;
    public float JumpHoldTime { get; init; } = 0.3f;

    /// <summary>Multiplies the player's gravity.</summary>
    public float GravityScale { get; init; } = 1f;

    public float MaxFallSpeed { get; init; } = 30f;

    /// <summary>Seconds of flight while jump is held in the air (refilled on the ground); 0 cannot fly, −1 flies freely.</summary>
    public float FlightTime { get; init; }

    /// <summary>Climb speed while flying (and descent speed with crouch for free fliers).</summary>
    public float FlySpeed { get; init; } = 6f;

    /// <summary>Free fliers hold their height when no key is pressed (the UFO).</summary>
    public bool Hover { get; init; }

    /// <summary>Damage dealt by landing on a creature (the Slimy Saddle); it bounces the mount back up.</summary>
    public int StompDamage { get; init; }

    /// <summary>Upward speed after a stomp.</summary>
    public float StompBounce { get; init; } = 11f;

    /// <summary>The creature model ridden (assets/models/creatures/&lt;model&gt;.glb), scaled to <see cref="Height"/> m tall, and its tint.</summary>
    public string Model { get; init; } = "";
    public float Height { get; init; } = 1.2f;
    public string? Tint { get; init; }

    /// <summary>Height of the saddle above the ground (m): the rider sits this far up (0: four fifths of the height).</summary>
    public float SeatHeight { get; init; }

    public float Seat => SeatHeight > 0f ? SeatHeight : Height * 0.8f;

    /// <summary>Buff shown while riding.</summary>
    public string? Buff { get; init; }

    public string? Validate() =>
        string.IsNullOrEmpty(Id) ? "mount needs an id"
        : RunSpeed <= 0 ? "mount runSpeed must be > 0"
        : FlightTime < -1 ? "mount flightTime must be ≥ −1"
        : string.IsNullOrEmpty(Model) ? "mount needs a model"
        : null;
}

/// <summary>A mount being ridden: its parameters and what is left of its flight.</summary>
public sealed class MountRide
{
    public MountRide(MountParams config, string itemId)
    {
        Config = config;
        ItemId = itemId;
        FlightLeft = config.FlightTime;
    }

    public MountParams Config { get; }

    /// <summary>The item that summoned it (using it again, or the quick-mount key, dismounts).</summary>
    public string ItemId { get; }

    /// <summary>Seconds of flight left (refilled on the ground); unused by free fliers.</summary>
    public float FlightLeft { get; internal set; }

    /// <summary>Seconds of light-gravity jump hold left.</summary>
    public float HoldLeft { get; internal set; }

    public bool Flying { get; internal set; }

    public bool FliesFreely => Config.FlightTime < 0;
}
