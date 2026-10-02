namespace Strata.Core.Content.Specs;

/// <summary>
/// A boss's free hand (Skeletron's): rests at its place on the body, then winds up and strikes through where the target
/// stood. The hand on the right (positive offset) waits half an interval before its first attack, so a pair alternates.
/// </summary>
public sealed class HandParams : SpecParams, IValidatableSpec
{
    public const string Alternate = "alternate", Slap = "slap", Swipe = "swipe", Saw = "saw", Vice = "vice", Hold = "hold";

    /// <summary>
    /// "slap" (drops from above), "swipe" (sweeps sideways), "alternate" (each attack the other, pairs out of step) —
    /// Skeletron's hands; Prime's tools: "saw" (rapid jabs at the target, at most <see cref="Reach"/> from its place),
    /// "vice" (creeps up on the target through the wind-up, then lunges) and "hold" (stays in place facing the target
    /// while its own <c>ranged</c> attack fires: the cannon and the laser).
    /// </summary>
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

    /// <summary>"saw": farthest a jab goes from the arm's place on the body (m).</summary>
    public float Reach { get; init; } = 8f;

    public string? Validate() =>
        Style is not (Alternate or Slap or Swipe or Saw or Vice or Hold) ? $"style '{Style}' must be alternate, slap, swipe, saw, vice or hold"
        : AttackInterval < 1 || WindupTicks < 1 || StrikeTicks < 1 ? "attackInterval, windupTicks and strikeTicks must be >= 1"
        : StrikeSpeed <= 0 || ReturnSpeed <= 0 || ExpertSpeed <= 0 || ExpertInterval <= 0 || Reach <= 0 ? "speeds, reach and Expert multipliers must be > 0" : null;
}
