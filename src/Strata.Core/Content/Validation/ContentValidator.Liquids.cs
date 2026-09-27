using Strata.Core.Content.Defs;
using Strata.Core.Content.Specs;

namespace Strata.Core.Content.Validation;

public static partial class ContentValidator
{
    private static void ValidateLiquids(Ctx ctx)
    {
        var codes = new Dictionary<int, LiquidDef>();
        foreach (var l in ctx.Db.Liquids.All)
        {
            if (l.Code is < 1 or > LiquidDef.MaxCode) ctx.Error(l, $"code must be 1–{LiquidDef.MaxCode}.");
            else if (!codes.TryAdd(l.Code, l)) ctx.Error(l, $"code {l.Code} is already used by '{codes[l.Code].Id}'.");
            if (l.FlowInterval < 1) ctx.Error(l, "flowInterval must be >= 1.");
            if (l.FallCells < 1) ctx.Error(l, "fallCells must be >= 1.");
            if (l.SpreadThreshold < 1) ctx.Error(l, "spreadThreshold must be >= 1.");
            if (l.MinLevel is < 1 or > Voxels.LiquidCode.MaxLevel) ctx.Error(l, $"minLevel must be 1–{Voxels.LiquidCode.MaxLevel}.");
            if (l.EvaporateChance is < 0 or > 1) ctx.Error(l, "evaporateChance must be 0–1.");
            if (l.MoveSpeed is <= 0 or > 2) ctx.Error(l, "moveSpeed must be in (0, 2].");
            if (l.Drag is <= 0 or > 1) ctx.Error(l, "drag must be in (0, 1].");
            if (l.ContactDamage < 0) ctx.Error(l, "contactDamage must be >= 0.");
            ctx.Light(l, l.Light, "light");
            ctx.Color(l, l.Color, "color");
            ctx.Ref(l, ctx.Db.Buffs, l.ContactBuff, "contactBuff");
            ctx.Ref(l, ctx.Db.Items, l.Bucket, "bucket");
            foreach (var mix in l.Mixes)
            {
                ctx.Ref(l, ctx.Db.Liquids, mix.With, "mixes.with");
                ctx.Ref(l, ctx.Db.Blocks, mix.Block, "mixes.block");
                if (mix.With == l.Id) ctx.Error(l, "a liquid cannot mix with itself.");
                if (ctx.Db.Blocks.TryGet(mix.Block, out var block) && !block.IsSolid) ctx.Error(l, $"mix block '{mix.Block}' must be solid.");
            }
        }
        var effects = ctx.Db.Buffs.All.SelectMany(b => b.Effects.Select(e => ((ContentDef)b, e)))
            .Concat(ctx.Db.Items.All.SelectMany(i => i.EquipEffects.Select(e => ((ContentDef)i, e))));
        foreach (var (owner, e) in effects.Where(x => x.e.Type == EffectSpecs.LiquidImmune))
            ctx.Ref(owner, ctx.Db.Liquids, e.Raw.TryGetProperty("liquid", out var lq) ? lq.GetString() : null, "liquidImmune.liquid");
        var conditions = ctx.Db.Recipes.All.SelectMany(r => r.Conditions.Select(c => ((ContentDef)r, c)))
            .Concat(ctx.Db.SpawnRules.All.SelectMany(r => r.Conditions.Select(c => ((ContentDef)r, c))))
            .Concat(ctx.Db.Events.All.SelectMany(e => e.Conditions.Select(c => ((ContentDef)e, c))));
        foreach (var (owner, c) in conditions.Where(x => x.c.Type == "nearLiquid"))
            ctx.Ref(owner, ctx.Db.Liquids, c.Raw.TryGetProperty("liquid", out var nl) ? nl.GetString() : "water", "nearLiquid.liquid");
        // World generation writes the built-in liquid codes; each needs a definition.
        foreach (var type in Enum.GetValues<Voxels.LiquidType>().Where(t => t != Voxels.LiquidType.None))
        {
            if (codes.ContainsKey((int)type)) continue;
            if (ctx.Db.Liquids.Count > 0) ctx.Report.Error("liquids", type.ToString(), $"world generation places liquid code {(int)type} ({type}) but no liquid defines it.");
            else ctx.Report.Warn("liquids", type.ToString(), "no liquids defined; generated liquids will be inert.");
        }
        foreach (var item in ctx.Db.Items.All)
            foreach (var spec in item.Behaviors.Where(b => b.Type == ItemBehaviorSpecs.Bucket))
            {
                string? held = spec.Raw.TryGetProperty("liquid", out var lp) ? lp.GetString() : null;
                string? empty = spec.Raw.TryGetProperty("empty", out var ep) ? ep.GetString() : null;
                ctx.Ref(item, ctx.Db.Liquids, held, "bucket.liquid");
                ctx.Ref(item, ctx.Db.Items, empty, "bucket.empty");
                if (spec.Raw.TryGetProperty("bottomless", out var bp) && bp.ValueKind == System.Text.Json.JsonValueKind.True) continue;
                if (held != null && empty == null) ctx.Error(item, "a filled bucket needs 'empty' (the item left after pouring).");
                if (held != null && ctx.Db.Liquids.TryGet(held, out var liquid) && liquid.Bucket != item.Id)
                    ctx.Error(item, $"bucket holds '{held}' but that liquid's bucket is '{liquid.Bucket}'.");
            }
    }
}
