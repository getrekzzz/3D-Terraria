namespace Strata.Core.Content.Specs;

/// <summary>
/// A boss's free hand (Skeletron's): rests at its place on the body, then winds up and strikes through where the target
/// stood. The hand on the right (positive offset) waits half an interval before its first attack, so a pair alternates.
/// </summary>
public sealed class HandParams : SpecParams, IValidatableSpec
{
    public const string Alternate = "alternate", Slap = "slap", Swipe = "swipe";

    /// <summary>"slap" (drops from above), "swipe" (sweeps sideways) or "alternate" (each attack the other, pairs out of step).</summary>
    public string Style { get; init; } = Alternate;

    /// <summary>Ticks resting between attacks.</summary>
    public int AttackInterval { get; init; } = 300;

    /// <summary>Ticks moving into the wind-up spot, then at most this many ticks striking.</summary>
    public int WindupTicks { get; init; } = 40;
    public int StrikeTicks { get; init; } = 45;

    /// <summary>Strike and return speeds (m/s); a swipe is 10% faster.</summary>
    public float StrikeSpeed { get; init; } = 16f;
    public float ReturnSpeed { get; init; } = 10f;

    /// <summary>Expert: speed multiplier and interval multiplier.</summary>
    public float ExpertSpeed { get; init; } = 1.25f;
    public float ExpertInterval { get; init; } = 0.7f;

    public string? Validate() =>
        Style is not (Alternate or Slap or Swipe) ? $"style '{Style}' must be alternate, slap or swipe"
        : AttackInterval < 1 || WindupTicks < 1 || StrikeTicks < 1 ? "attackInterval, windupTicks and strikeTicks must be >= 1"
        : StrikeSpeed <= 0 || ReturnSpeed <= 0 || ExpertSpeed <= 0 || ExpertInterval <= 0 ? "speeds and Expert multipliers must be > 0" : null;
}
