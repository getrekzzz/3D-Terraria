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
            case AchievementSpecs.TalkTo:
                ctx.Ref(owner, ctx.Db.TownNpcs, spec.Parse<TalkTrigger>().Resident, $"{field}.resident");
                break;
            case AchievementSpecs.Bestiary when spec.Parse<CountTrigger>().Min > 100:
                ctx.Error(owner, $"{field}.min is a percent (1–100).");
                break;
        }
    }
}
