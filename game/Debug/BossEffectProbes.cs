using Strata.Core.Commands;
using Strata.Game.Audio;
using Strata.Game.Player;

namespace Strata.Game.Debug;

/// <summary>Probes on boss presentation: the camera's shake trauma and whether the soundtrack is held silent after a win.</summary>
[ProbeSource]
public static class BossEffectProbes
{
    public static void Register(CommandContext ctx)
    {
        ctx.AddProbe("camera.trauma", () => CameraShake.Trauma);
        ctx.AddProbe("music.silent", () => MusicDirector.Instance?.Silent == true ? 1 : 0);
    }
}
