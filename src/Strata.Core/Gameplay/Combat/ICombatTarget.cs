using System.Numerics;
using Strata.Core.Common;

namespace Strata.Core.Gameplay.Combat;

/// <summary>
/// Something a hostile creature can chase and attack: a player today, town residents later. Brains steer by it; effects
/// that only make sense on a player (confusion, the tongue) go through <see cref="AI.BrainContext.TargetPlayer"/>.
/// </summary>
public interface ICombatTarget
{
    /// <summary>Feet position in metres.</summary>
    Vector3 Position { get; }

    Vector3 Center { get; }

    /// <summary>Eye position (sight checks).</summary>
    Vector3 Eye { get; }

    Vector3 Velocity { get; }

    /// <summary>The cell containing the feet (condition checks such as enrage and despawn).</summary>
    Int3 FeetCell { get; }

    bool Alive { get; }
}
