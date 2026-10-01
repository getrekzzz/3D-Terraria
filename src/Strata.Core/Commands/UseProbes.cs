namespace Strata.Core.Commands;

/// <summary>
/// Item-use probes: <c>player.swings</c> counts the use animations started since the last <c>mark</c> (one per swing,
/// never restarted mid-arc), <c>player.use_anim</c> is the length (ticks) of the latest one after speed bonuses, and
/// <c>use.failures</c> counts the failed presses reported so far (no ammo, no mana, refused with a reason; never a held
/// button).
/// </summary>
[ProbeSource]
internal static class UseProbes
{
    public static void Register(CommandContext ctx)
    {
        ctx.AddProbe("player.swings", () => ctx.Player.UseSerial - ctx.MarkSwings);
        ctx.AddProbe("player.use_anim", () => ctx.Player.UseAnimationMax);
        ctx.AddProbe("use.failures", () => ctx.World.ItemUse.Failures);
    }
}
