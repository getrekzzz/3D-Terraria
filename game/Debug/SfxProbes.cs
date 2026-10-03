using Strata.Core.Commands;
using Strata.Game.Bootstrap;

namespace Strata.Game.Debug;

/// <summary>
/// Sound effect voice probes (P3-27): <c>sfx.voices</c> (voices sounding now across the 2D and 3D pools),
/// <c>sfx.peak</c> (the most one pool ever had sounding at once, never above <c>sfx.cap</c>), <c>sfx.cap</c> (voices per
/// pool), <c>sfx.stolen</c> (voices cut short for a newer sound) and <c>sfx.far</c> (world sounds skipped as out of
/// hearing).
/// </summary>
[ProbeSource]
public static class SfxProbes
{
    public static void Register(CommandContext ctx, LaunchedWorld world)
    {
        ctx.AddProbe("sfx.voices", () => Audio.Sfx.Busy);
        ctx.AddProbe("sfx.peak", () => Audio.Sfx.PeakBusy);
        ctx.AddProbe("sfx.cap", () => Audio.Sfx.Voices);
        ctx.AddProbe("sfx.stolen", () => Audio.Sfx.Stolen);
        ctx.AddProbe("sfx.far", () => Audio.Sfx.SkippedFar);
    }
}
