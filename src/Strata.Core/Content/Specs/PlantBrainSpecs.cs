namespace Strata.Core.Content.Specs;

/// <summary>
/// Plantera's hook (<see cref="AI.PlanteraHookBrain"/>): latches onto solid blocks within <see cref="Reach"/> m of the
/// target and moves to a new grip every <see cref="RelocateInterval"/> ticks (± a quarter) at <see cref="Speed"/> m/s.
/// </summary>
public sealed class PlanteraHookParams : SpecParams, IValidatableSpec
{
    public float Reach { get; init; } = 14f;
    public int RelocateInterval { get; init; } = 180;
    public float Speed { get; init; } = 12f;

    public string? Validate() => Reach <= 0 || Speed <= 0 || RelocateInterval < 1 ? "reach and speed must be > 0, relocateInterval >= 1" : null;
}
