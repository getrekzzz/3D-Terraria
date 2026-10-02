using Strata.Core.Commands;
using Strata.Game.Bootstrap;

namespace Strata.Game.Debug;

/// <summary>Effect probes: <c>effects.bursts</c> (EffectKit bursts started so far) <c>effects.gore</c> (gore chunks alive) and
/// <c>effects.cracks</c> (crack decals on damaged blocks), <c>player.splashes</c> (liquid entries and exits splashed),
/// <c>fx.ragdolls</c> / <c>fx.ragdolls_started</c> (human ragdolls lying now / so far), <c>effects.tree_falls</c> (trees toppled), <c>camera.punches</c> (camera jolts) and <c>hud.hurt_flashes</c> (red hurt flashes).</summary>
[ProbeSource]
public static class EffectProbes
{
    public static void Register(CommandContext ctx, LaunchedWorld world)
    {
        ctx.AddProbe("effects.bursts", () => Rendering.EffectKit.Instance?.Bursts ?? 0);
        ctx.AddProbe("effects.gore", () => Entities.GoreSystem.Instance?.Live ?? 0);
        ctx.AddProbe("fx.ragdolls", () => world.Runtime.GetNodeOrNull<Entities.NpcRenderer>("Creatures")?.Ragdolls ?? 0);
        // How far the last ragdoll's hips have dropped since it started (metres): a crumpled body is well below a standing one.
        ctx.AddProbe("fx.ragdoll_drop", () => world.Runtime.GetNodeOrNull<Entities.NpcRenderer>("Creatures")?.LastRagdoll?.Dropped ?? 0f);
        ctx.AddProbe("avatar.ragdoll", () => world.Avatar?.Ragdolled == true ? 1 : 0);
        ctx.AddProbe("fx.ragdolls_started", () => world.Runtime.GetNodeOrNull<Entities.NpcRenderer>("Creatures")?.RagdollsStarted ?? 0);
        ctx.AddProbe("effects.cracks", () => Rendering.BlockCracks.Instance?.Count ?? 0);
        ctx.AddProbe("effects.tree_falls", () => world.Runtime.GetNodeOrNull<World.TreeEffects>("TreeEffects")?.Falls ?? 0);
        ctx.AddProbe("camera.punches", () => Player.CameraShake.Punches);
        ctx.AddProbe("hud.hurt_flashes", () => world.Runtime.GetNodeOrNull<UI.HurtOverlay>("HurtOverlay")?.Flashes ?? 0);
        ctx.AddProbe("player.splashes", () => world.Player?.GetNodeOrNull<Player.PlayerDust>("PlayerDust")?.Splashes ?? 0);
    }
}
