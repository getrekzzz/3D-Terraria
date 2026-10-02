namespace Strata.Core.Content.Validation;

public static partial class ContentValidator
{
    /// <summary>Onboarding hints (data/hints.json): text, conditions and every id they name.</summary>
    private static void ValidateHints(Ctx ctx)
    {
        foreach (var h in ctx.Db.Hints.All)
        {
            if (string.IsNullOrWhiteSpace(h.Text)) ctx.Error(h, "text is required.");
            if (h.Text.Split('{').Length != h.Text.Split('}').Length) ctx.Error(h, "text has unbalanced { } placeholders.");
            if (h.Duration <= 0f) ctx.Error(h, "duration must be positive.");
            if (h.Delay < 0f) ctx.Error(h, "delay cannot be negative.");
            for (int i = 0; i < h.When.Length; i++) ValidateTrigger(ctx, h, h.When[i], $"when[{i}]");
            for (int i = 0; i < h.Unless.Length; i++) ValidateTrigger(ctx, h, h.Unless[i], $"unless[{i}]");
            ctx.Ref(h, ctx.Db.Items, h.Carrying, "carrying");
            ctx.Ref(h, ctx.Db.GuideSteps, h.Step, "step");
            if (h.Guide != null && !ctx.Db.GuideSteps.Contains(h.Guide) && !ctx.Db.GuideTips.Contains(h.Guide))
                ctx.Error(h, $"guide '{h.Guide}' is neither a guide step nor a guide tip.");
            if (h.Near is { } near)
            {
                ctx.Ref(h, ctx.Db.TileObjects, near.Object, "near.object");
                ctx.Ref(h, ctx.Db.TownNpcs, near.Resident, "near.resident");
                if (near.Tag != null && !ctx.Db.TileObjects.All.Any(t => t.HasTag(near.Tag))) ctx.Error(h, $"near.tag '{near.Tag}' is on no tile object.");
                if (near.Object == null && near.Tag == null && near.Resident == null && !near.Tree) ctx.Error(h, "near names nothing.");
                if (near.Radius <= 0f || near.Radius > 32f) ctx.Error(h, "near.radius must be in (0, 32] metres.");
            }
        }
    }
}
