using Strata.Core.Commands;
using Strata.Game.Bootstrap;
using Strata.Game.Entities;

namespace Strata.Game.Debug;

/// <summary>
/// <c>town.&lt;id&gt;.clip</c>: the clip a resident's body actually plays, as an index in HumanoidAnimator.States
/// (0 idle, 1 walk, … sit, sleep, bound, lie); -1 not shown, -2 no humanoid set. <c>town.&lt;id&gt;.hips</c>: the hips bone's height above the feet as posed (lower when sitting or kneeling). <c>town.&lt;id&gt;.tag</c>: the name tag's alpha (0 hidden: a screen is open, or the camera is right beside it). Core's <c>town.</c> probes answer the rest.
/// </summary>
[ProbeSource]
public static class TownClipProbes
{
    public static void Register(CommandContext ctx, LaunchedWorld world, GodotLog log)
    {
        ctx.AddPrefixProbe("town.", spec =>
        {
            var parts = spec.Split('.');
            if (parts is not [var id, "clip" or "hips" or "tag"]) return null;
            var residents = world.Runtime.GetNodeOrNull<TownRenderer>("Residents");
            if (residents == null) return -1;
            return parts[1] switch { "clip" => residents.PlayingClip(id), "tag" => residents.TagAlpha(id), _ => residents.HipsHeight(id) };
        });
    }
}
