namespace Strata.Core.Commands;

/// <summary>
/// Item-use probes: <c>player.swings</c> counts the use animations started so far (one per swing, never restarted
/// mid-arc) and <c>player.use_anim</c> is the length (ticks) of the latest one after speed bonuses.
/// </summary>
[ProbeSource]
internal static class UseProbes
{
    public static void Register(CommandContext ctx)
    {
        ctx.AddProbe("player.swings", () => ctx.Player.UseSerial);
        ctx.AddProbe("player.use_anim", () => ctx.Player.UseAnimationMax);
    }
}
