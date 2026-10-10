using Strata.Core.Content.Specs;

namespace Strata.Core.Content.Validation;

public static partial class ContentValidator
{
    private static void ValidateGuide(Ctx ctx)
    {
        foreach (var a in ctx.Db.Achievements.All)
        {
            if (string.IsNullOrWhiteSpace(a.Name)) ctx.Error(a, "name is required.");
            if (string.IsNullOrWhiteSpace(a.Description)) ctx.Error(a, "description is required.");
            ctx.Ref(a, ctx.Db.Items, a.Icon, "icon");
            ValidateTrigger(ctx, a, a.Trigger, "trigger");
        }
        foreach (var s in ctx.Db.GuideSteps.All)
        {
            if (string.IsNullOrWhiteSpace(s.Text)) ctx.Error(s, "text is required.");
            if (s.DoneWhen.Length == 0) ctx.Error(s, "doneWhen must list at least one trigger.");
            foreach (var after in s.After) ctx.Ref(s, ctx.Db.GuideSteps, after, "after");
            for (int i = 0; i < s.DoneWhen.Length; i++) ValidateTrigger(ctx, s, s.DoneWhen[i], $"doneWhen[{i}]");
            for (int i = 0; i < s.ShowWhen.Length; i++) ValidateTrigger(ctx, s, s.ShowWhen[i], $"showWhen[{i}]");
            foreach (var item in s.RelatedItems) ctx.Ref(s, ctx.Db.Items, item, "relatedItems");
            foreach (var enemy in s.RelatedEnemies) ctx.Ref(s, ctx.Db.Enemies, enemy, "relatedEnemies");
        }
        DetectCycles(ctx, ctx.Db.GuideSteps.All, s => s.After, "guide step");
        foreach (var t in ctx.Db.GuideTips.All)
        {
            if (string.IsNullOrWhiteSpace(t.Text)) ctx.Error(t, "text is required.");
            if (t.Text.Split('{').Length != t.Text.Split('}').Length) ctx.Error(t, "text has unbalanced { } placeholders.");
            if (t.Topic == Defs.GuideTipTopic.Controls && t.Action == null) ctx.Error(t, "a controls tip names its input action.");
        }
    }

    /// <summary>An achievement trigger (or guide step condition): its shape and every id it names.</summary>
    private static void ValidateTrigger(Ctx ctx, ContentDef owner, TypedSpec spec, string field)
    {
        ctx.Spec(owner, SpecKind.AchievementTrigger, spec, field);
        if (ctx.Options.Specs.Validate(SpecKind.AchievementTrigger, spec) != null) return;
        switch (spec.Type)
        {
            case AchievementSpecs.Flag:
                var flag = spec.Parse<FlagTrigger>();
                ctx.Ref(owner, ctx.Db.Flags, flag.Id, $"{field}.id");
                foreach (var f in flag.All) ctx.Ref(owner, ctx.Db.Flags, f, $"{field}.all");
                break;
            case AchievementSpecs.ObtainItem or AchievementSpecs.CraftItem:
                var item = spec.Parse<ItemTrigger>();
                ctx.Ref(owner, ctx.Db.Items, item.Item, $"{field}.item");
                foreach (var id in item.Items ?? Array.Empty<string>()) ctx.Ref(owner, ctx.Db.Items, id, $"{field}.items");
                if (item.Tag != null && !ctx.Db.Items.All.Any(i => i.HasTag(item.Tag))) ctx.Error(owner, $"{field}.tag '{item.Tag}' is on no item.");
                break;
            case AchievementSpecs.KillEnemy:
                var kill = spec.Parse<KillTrigger>();
                ctx.Ref(owner, ctx.Db.Enemies, kill.Enemy, $"{field}.enemy");
                if (kill.Tag != null && !ctx.Db.Enemies.All.Any(e => e.HasTag(kill.Tag))) ctx.Error(owner, $"{field}.tag '{kill.Tag}' is on no enemy.");
                break;
            case AchievementSpecs.MineBlock or AchievementSpecs.BreakObject:
                var brk = spec.Parse<BreakTrigger>();
                ctx.Ref(owner, ctx.Db.Blocks, brk.Block, $"{field}.block");
                ctx.Ref(owner, ctx.Db.TileObjects, brk.Object, $"{field}.object");
                bool blocks = spec.Type == AchievementSpecs.MineBlock;
                if (blocks && brk.Object != null) ctx.Error(owner, $"{field}: mineBlock takes block, not object.");
                if (!blocks && brk.Block != null) ctx.Error(owner, $"{field}: breakObject takes object, not block.");
                if (brk.Tag != null && !(blocks ? ctx.Db.Blocks.All.Any(b => b.HasTag(brk.Tag)) : ctx.Db.TileObjects.All.Any(t => t.HasTag(brk.Tag))))
                    ctx.Error(owner, $"{field}.tag '{brk.Tag}' is on no {(blocks ? "block" : "tile object")}.");
                break;
            case AchievementSpecs.Rescued:
                ctx.Ref(owner, ctx.Db.TownNpcs, spec.Parse<RescuedTrigger>().Resident, $"{field}.resident");
                break;
            case AchievementSpecs.TalkTo:
                ctx.Ref(owner, ctx.Db.TownNpcs, spec.Parse<TalkTrigger>().Resident, $"{field}.resident");
                break;
            case AchievementSpecs.Bestiary when spec.Parse<CountTrigger>().Min > 100:
                ctx.Error(owner, $"{field}.min is a percent (1–100).");
                break;
        }
    }
}
