using System.Text.RegularExpressions;
using Strata.Core.Content.Defs;
using Strata.Core.Content.Specs;

namespace Strata.Core.Content.Validation;

public static partial class ContentValidator
{
    private static readonly Regex ResidentPlaceholder = new(@"\{resident:([a-z0-9_]+)\}", RegexOptions.Compiled);

    private static void ValidateTown(Ctx ctx)
    {
        foreach (var t in ctx.Db.TownNpcs.All)
        {
            if (t.Names.Length == 0) ctx.Error(t, "names must list at least one personal name.");
            if (t.Life < 1) ctx.Error(t, "life must be >= 1.");
            if (t.Speed <= 0) ctx.Error(t, "speed must be > 0.");
            if (t.Lines.Length == 0) ctx.Error(t, "lines must contain at least one dialogue line.");
            if (!t.Starter && t.Landmark == null && t.Arrival.Length == 0) ctx.Warn(t, "no arrival conditions: arrives as soon as a home exists.");
            ctx.Asset(t, AssetPaths.TownNpcModel(t), "model");
            ctx.Specs(t, SpecKind.Condition, t.Arrival, "arrival");
            for (int i = 0; i < t.Shop.Length; i++)
            {
                var e = t.Shop[i];
                ctx.Ref(t, ctx.Db.Items, e.Item, $"shop[{i}].item");
                if (e.Price is < 0) ctx.Error(t, $"shop[{i}].price must be >= 0.");
                ctx.Specs(t, SpecKind.Condition, e.Conditions, $"shop[{i}].conditions");
            }
            if (t.Service is { } service)
            {
                ctx.Spec(t, SpecKind.TownService, service, "service");
                if (service.Type == TownServiceSpecs.SummonBoss && ctx.TryParse<SummonServiceParams>(SpecKind.TownService, service) is { } summon)
                {
                    ctx.Ref(t, ctx.Db.Enemies, summon.Enemy, "service.enemy");
                    if (ctx.Db.Enemies.TryGet(summon.Enemy, out var boss) && boss.Boss == null) ctx.Error(t, $"service.enemy '{summon.Enemy}' is not a boss.");
                    ctx.Specs(t, SpecKind.Condition, summon.Conditions, "service.conditions");
                }
            }
            if (t.Landmark != null)
            {
                if (!WorldGen.Plan.LandmarkIds.All.Contains(t.Landmark)) ctx.Error(t, $"landmark '{t.Landmark}' is not a place world generation plans ({string.Join(", ", WorldGen.Plan.LandmarkIds.All)}).");
                if (t.Starter) ctx.Error(t, "a landmark resident cannot also be a starter.");
            }
            ctx.Specs(t, SpecKind.Condition, t.LeavesWhen, "leavesWhen");
            for (int i = 0; i < t.Lines.Length; i++)
            {
                var line = t.Lines[i];
                if (string.IsNullOrWhiteSpace(line.Text)) ctx.Error(t, $"lines[{i}] is empty.");
                ctx.Specs(t, SpecKind.Condition, line.Conditions, $"lines[{i}].conditions");
                foreach (Match m in ResidentPlaceholder.Matches(line.Text))
                    ctx.Ref(t, ctx.Db.TownNpcs, m.Groups[1].Value, $"lines[{i}] placeholder");
            }
            foreach (var m in t.Mood)
            {
                if ((m.Biome == null) == (m.Neighbor == null)) ctx.Error(t, "each mood entry needs exactly one of 'biome' or 'neighbor'.");
                if (m.Score is < -2 or > 2 or 0) ctx.Error(t, "mood score must be −2, −1, 1 or 2.");
                ctx.Ref(t, ctx.Db.Biomes, m.Biome, "mood.biome");
                ctx.Ref(t, ctx.Db.TownNpcs, m.Neighbor, "mood.neighbor");
            }
            foreach (var c in AllConditions(t)) CheckTownCondition(ctx, t, c);
        }
        foreach (var r in ctx.Db.SpawnRules.All)
            foreach (var c in r.Conditions) CheckTownCondition(ctx, r, c);
        foreach (var r in ctx.Db.Recipes.All)
            foreach (var c in r.Conditions) CheckTownCondition(ctx, r, c);
    }

    private static IEnumerable<TypedSpec> AllConditions(TownNpcDef t) =>
        t.Arrival.Concat(t.LeavesWhen).Concat(t.Shop.SelectMany(s => s.Conditions)).Concat(t.Lines.SelectMany(l => l.Conditions));

    /// <summary>References inside resident/hasItem conditions (also nested in all/any/not).</summary>
    private static void CheckTownCondition(Ctx ctx, ContentDef owner, TypedSpec c)
    {
        string? Str(string name) => c.Raw.TryGetProperty(name, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String ? v.GetString() : null;
        switch (c.Type)
        {
            case "resident":
                ctx.Ref(owner, ctx.Db.TownNpcs, Str("id"), "resident condition");
                break;
            case "hasItem":
                ctx.Ref(owner, ctx.Db.Items, Str("item"), "hasItem condition");
                break;
            case "all" or "any" or "not" when c.Raw.TryGetProperty("of", out var of) && of.ValueKind == System.Text.Json.JsonValueKind.Array:
                foreach (var inner in of.EnumerateArray())
                    if (inner.TryGetProperty("type", out var type) && type.GetString() is { } tn)
                        CheckTownCondition(ctx, owner, new TypedSpec(tn, inner.Clone()));
                break;
        }
    }
}
