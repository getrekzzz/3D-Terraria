using Strata.Core.Commands;
using Strata.Game.Bootstrap;

namespace Strata.Game.Debug;

/// <summary>Effect probes: <c>effects.bursts</c> (EffectKit bursts started so far) <c>effects.gore</c> (gore chunks alive) and
/// <c>effects.cracks</c> (crack decals on damaged blocks), <c>player.splashes</c> (liquid entries and exits splashed),
/// <c>camera.punches</c> (camera jolts) and <c>hud.hurt_flashes</c> (red hurt flashes).</summary>
[ProbeSource]
public static class EffectProbes
{
    public static void Register(CommandContext ctx, LaunchedWorld world)
    {
        ctx.AddProbe("effects.bursts", () => Rendering.EffectKit.Instance?.Bursts ?? 0);
        ctx.AddProbe("effects.gore", () => Entities.GoreSystem.Instance?.Live ?? 0);
        ctx.AddProbe("effects.cracks", () => Rendering.BlockCracks.Instance?.Count ?? 0);
        ctx.AddProbe("camera.punches", () => Player.CameraShake.Punches);
        ctx.AddProbe("hud.hurt_flashes", () => world.Runtime.GetNodeOrNull<UI.HurtOverlay>("HurtOverlay")?.Flashes ?? 0);
        ctx.AddProbe("player.splashes", () => world.Player?.GetNodeOrNull<Player.PlayerDust>("PlayerDust")?.Splashes ?? 0);
    }
}
