using Strata.Core.Content.Defs;
using Strata.Core.Content.Specs;

namespace Strata.Core.Content.Validation;

public static partial class ContentValidator
{
    private static void ValidateBiomes(Ctx ctx)
    {
        foreach (var b in ctx.Db.Biomes.All)
        {
            ctx.Ref(b, ctx.Db.Flags, b.LockedUntil, "lockedUntil");
            ctx.Color(b, b.MapColor, "mapColor");
            ctx.Color(b, b.FogColor, "fogColor");
        }
    }

    private static void ValidateEvents(Ctx ctx)
    {
        foreach (var e in ctx.Db.Events.All)
        {
            if (e.Chance is < 0 or > 1) ctx.Error(e, "chance must be 0–1.");
            if (e.Phase == EventPhase.Manual && e.Duration <= 0 && e.KillGoal <= 0) ctx.Error(e, "manual events need a duration or a kill goal.");
            if (e.KillGoal > 0 && string.IsNullOrEmpty(e.KillTag)) ctx.Error(e, "killGoal needs killTag.");
            if (e.SpawnRateMultiplier <= 0) ctx.Error(e, "spawnRateMultiplier must be > 0.");
            ctx.Ref(e, ctx.Db.Enemies, e.FinaleEnemy, "finaleEnemy");
            ctx.Ref(e, ctx.Db.Flags, e.CompletionFlag, "completionFlag");
            ctx.Ref(e, ctx.Db.Flags, e.StartsWhen, "startsWhen");
            foreach (var placed in e.Places) ctx.Ref(e, ctx.Db.Enemies, placed, "places");
            if (e.StartsWhen != null && e.Phase != EventPhase.Manual) ctx.Error(e, "startsWhen is for manual events.");
            ctx.Color(e, e.SkyTint, "skyTint");
            ctx.Specs(e, SpecKind.Condition, e.Conditions, "conditions");
        }
        foreach (var s in ctx.Db.SpawnRules.All)
            foreach (var c in s.Conditions.Where(c => c.Type == "event"))
            {
                var id = c.Raw.TryGetProperty("id", out var v) ? v.GetString() : null;
                if (id != null && !ctx.Db.Events.Contains(id)) ctx.Error(s, $"event condition references unknown event '{id}'.");
            }
    }

    private static void ValidateFlags(Ctx ctx)
    {
        foreach (var f in ctx.Db.Flags.All)
        {
            foreach (var r in f.Requires)
                ctx.Ref(f, ctx.Db.Flags, r, "requires");
            if (f.AutoSet && f.Requires.Length == 0) ctx.Error(f, "autoSet needs at least one required flag.");
        }
        DetectCycles(ctx, ctx.Db.Flags.All, f => f.Requires, "flag");
    }

    private static void DetectCycles<T>(Ctx ctx, IReadOnlyList<T> defs, Func<T, IEnumerable<string>> edges, string what) where T : ContentDef
    {
        var byId = defs.ToDictionary(d => d.Id);
        var state = new Dictionary<string, int>(); // 1 = visiting, 2 = done
        foreach (var d in defs) Visit(d.Id);

        void Visit(string id)
        {
            if (!byId.TryGetValue(id, out var def)) return;
            if (state.TryGetValue(id, out var s))
            {
                if (s == 1) ctx.Error(def, $"{what} dependency cycle through '{id}'.");
                return;
            }
            state[id] = 1;
            foreach (var next in edges(def)) Visit(next);
            state[id] = 2;
        }
    }

    private static void ValidateObtainability(Ctx ctx)
    {
        var sources = new HashSet<string>(StringComparer.Ordinal);
        foreach (var r in ctx.Db.Recipes.All) sources.Add(r.Output);
        foreach (var t in ctx.Db.LootTables.All)
            foreach (var e in t.Entries)
                if (e.Item != null) sources.Add(e.Item);
        foreach (var d in ctx.Db.BlockDrops)
            if (d != null) sources.Add(d.Id);
        foreach (var t in ctx.Db.TileObjects.All)
        {
            // Breaking a world-generated object yields its placing item.
            if (t.PlacingItemId != null && (t.Natural || t.HasTag("world"))) sources.Add(t.PlacingItemId);
        }
        // A bug net turns critters into their catch items.
        foreach (var e in ctx.Db.Enemies.All)
            if (e.CatchItem != null) sources.Add(e.CatchItem);
        // Residents sell their stock.
        foreach (var t in ctx.Db.TownNpcs.All)
            foreach (var e in t.Shop) sources.Add(e.Item);
        // Filling an obtainable empty bucket yields the liquid's bucket.
        if (ctx.Db.Items.All.Any(i => sources.Contains(i.Id) && i.Behaviors.Any(b => b.Type == Specs.ItemBehaviorSpecs.Bucket && !b.Raw.TryGetProperty("liquid", out _))))
            foreach (var l in ctx.Db.Liquids.All)
                if (l.Bucket != null) sources.Add(l.Bucket);
        bool anyCoins = ctx.Db.LootTables.All.Any(t => t.Coins != null);

        foreach (var i in ctx.Db.Items.All)
        {
            if (sources.Contains(i.Id) || i.HasTag("starter") || i.HasTag("shop") || i.HasTag("debug") || i.HasTag("world")) continue;
            if (i.HasTag("coin") && anyCoins) continue;
            ctx.Warn(i, "item has no recipe, loot, block drop or tile-object source (tag 'shop'/'starter' if intended).");
        }
    }
}
