using Strata.Core.Content.Defs;
using Strata.Core.Content.Specs;

namespace Strata.Core.Content.Validation;

public static partial class ContentValidator
{
    /// <summary>Rescue blocks: their conditions, the pose, the landmark and the item a rescue needs.</summary>
    private static void ValidateTownRescue(Ctx ctx, TownNpcDef t)
    {
        if (t.Rescue is not { } r) return;
        ctx.Specs(t, SpecKind.Condition, r.Requires, "rescue.requires");
        ctx.Specs(t, SpecKind.Condition, r.Where, "rescue.where");
        foreach (var c in r.Requires.Concat(r.Where)) CheckTownCondition(ctx, t, c);
        if (Array.IndexOf(RescueInfo.Poses, r.Pose) < 0) ctx.Error(t, $"rescue.pose '{r.Pose}' must be one of {string.Join(", ", RescueInfo.Poses)}.");
        if (r.Landmark != null && !WorldGen.Plan.LandmarkIds.All.Contains(r.Landmark)) ctx.Error(t, $"rescue.landmark '{r.Landmark}' is not a planned landmark.");
        if (r.Landmark == null && r.Where.Length == 0) ctx.Warn(t, "rescue has neither a landmark nor where-conditions: it may appear anywhere.");
        ctx.Ref(t, ctx.Db.Items, r.RequiresItem, "rescue.requiresItem");
        if (string.IsNullOrWhiteSpace(r.Title)) ctx.Error(t, "rescue.title is required (the name shown while bound).");
        if (r.MinDistance < 0 || r.MaxDistance < r.MinDistance) ctx.Error(t, "rescue distances must satisfy 0 <= minDistance <= maxDistance.");
        if (t.Starter || t.Landmark != null) ctx.Error(t, "a starter or landmark resident cannot be rescued.");
    }
}
