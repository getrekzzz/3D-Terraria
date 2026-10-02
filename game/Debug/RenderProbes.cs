using Strata.Core.Commands;
using Strata.Game.Bootstrap;

namespace Strata.Game.Debug;

/// <summary>
/// Probes on the renderer's adaptive state: the eye's exposure (the CPU model, so it also runs headless) and how often
/// the dynamic resolution changed (<c>render.scale_changes</c>; always 0 headless).
/// </summary>
[ProbeSource]
public static class RenderProbes
{
    public static void Register(CommandContext ctx, LaunchedWorld world)
    {
        var runtime = world.Runtime;
        ctx.AddProbe("render.exposure", () => runtime.Environment.Exposure);
        ctx.AddProbe("render.scale_changes", () => Rendering.DynamicResolution.Changes);
    }
}
