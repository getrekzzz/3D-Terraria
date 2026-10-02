using Strata.Core.Content.Defs;

namespace Strata.Game.Player;

/// <summary>Movement moments for effects (PlayerDust): landings, jumps, footsteps, and going into or out of a liquid.</summary>
public sealed partial class PlayerController
{
    /// <summary>Touched down, with the downward speed of the impact (m/s).</summary>
    public event System.Action<float>? Landed;

    /// <summary>Left the ground upwards.</summary>
    public event System.Action? Jumped;

    /// <summary>A step while walking or running, with the ground speed (m/s).</summary>
    public event System.Action<float>? Footstep;

    /// <summary>The chest went into a liquid, with the vertical speed at that moment.</summary>
    public event System.Action<LiquidDef, float>? LiquidEntered;

    /// <summary>The chest came out of a liquid, with the vertical speed at that moment.</summary>
    public event System.Action<LiquidDef, float>? LiquidExited;

    private void LiquidEdge(LiquidDef? from, LiquidDef? to)
    {
        if (from != null) LiquidExited?.Invoke(from, Velocity.Y);
        if (to != null) LiquidEntered?.Invoke(to, Velocity.Y);
    }
}
