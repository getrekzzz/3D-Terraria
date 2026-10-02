using Strata.Core.Commands;
using Strata.Game.Bootstrap;
using Strata.Game.Entities;

namespace Strata.Game.Debug;

/// <summary>
/// <c>town.&lt;id&gt;.clip</c>: the clip a resident's body actually plays, as an index in HumanoidAnimator.States
/// (0 idle, 1 walk, … sit, sleep, bound, lie); -1 not shown, -2 no humanoid set. <c>town.&lt;id&gt;.hips</c>: the hips bone's height above the feet as posed (lower when sitting or kneeling). Core's <c>town.</c> probes answer the rest.
/// </summary>
[ProbeSource]
public static class TownClipProbes
{
    public static void Register(CommandContext ctx, LaunchedWorld world, GodotLog log)
    {
        ctx.AddPrefixProbe("town.", spec =>
        {
            var parts = spec.Split('.');
            if (parts is not [var id, "clip" or "hips"]) return null;
            var residents = world.Runtime.GetNodeOrNull<TownRenderer>("Residents");
            if (residents == null) return -1;
            return parts[1] == "clip" ? residents.PlayingClip(id) : residents.HipsHeight(id);
        });
    }
}
