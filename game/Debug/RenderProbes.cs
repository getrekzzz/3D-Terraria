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
    }
}
