using Strata.Core.Commands;
using Strata.Game.Bootstrap;

namespace Strata.Game.Debug;

/// <summary>
/// Probes on the renderer's adaptive state: the eye's exposure (the CPU model, so it also runs headless) and how often
/// the dynamic resolution changed (<c>render.scale_changes</c>; always 0 headless); light pets in the light pool
/// (<c>lights.pets</c>).
/// </summary>
[ProbeSource]
public static class RenderProbes
{
    public static void Register(CommandContext ctx, LaunchedWorld world)
    {
        var runtime = world.Runtime;
        ctx.AddProbe("render.exposure", () => runtime.Environment.Exposure);
        ctx.AddProbe("render.scale_changes", () => Rendering.DynamicResolution.Changes);
        // Light pets holding a light from the dynamic pool (capped by the preset's petLights).
        ctx.AddProbe("lights.pets", () => runtime.GetNodeOrNull<Entities.ProjectileRenderer>("Projectiles")?.PetLightsOffered ?? -1);
        ctx.AddProbe("projectiles.nodes_created", () => runtime.GetNodeOrNull<Entities.ProjectileRenderer>("Projectiles")?.NodesCreated ?? -1);
        ctx.AddProbe("projectiles.pooled", () => runtime.GetNodeOrNull<Entities.ProjectileRenderer>("Projectiles")?.Pooled ?? -1);
        ctx.AddProbe("projectiles.lights", () => runtime.GetNodeOrNull<Entities.ProjectileRenderer>("Projectiles")?.LightsUsed ?? -1);
        ctx.AddProbe("fx.impacts", () => runtime.GetNodeOrNull<Entities.CombatEffects>("CombatEffects")?.Impacts ?? -1);
        ctx.AddProbe("whips.drawn", () => runtime.GetNodeOrNull<Entities.WhipRenderer>("Whips")?.Drawn ?? -1);
        ctx.AddProbe("whips.cracks", () => runtime.GetNodeOrNull<Entities.WhipRenderer>("Whips")?.Cracks ?? -1);
        ctx.AddProbe("fx.muzzle_flashes", () => runtime.GetNodeOrNull<Entities.CombatEffects>("CombatEffects")?.Muzzle.Shown ?? -1);
        ctx.AddProbe("projectiles.trails", () => runtime.GetNodeOrNull<Entities.ProjectileRenderer>("Projectiles")?.TrailsDrawn ?? -1);
        ctx.AddProbe("projectiles.strings", () => runtime.GetNodeOrNull<Entities.ProjectileRenderer>("Projectiles")?.StringsDrawn ?? -1);
        ctx.AddProbe("projectiles.from_muzzle", () => runtime.GetNodeOrNull<Entities.ProjectileRenderer>("Projectiles")?.FromMuzzle ?? -1);
    }
}
