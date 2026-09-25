using Strata.Core.Content.Defs;
using Strata.Core.Content.Specs;

namespace Strata.Core.Content.Validation;

public static partial class ContentValidator
{
    private static readonly HashSet<string> PrefixGroups = new() { "melee", "ranged", "magic", "summon", "tool", "accessory", "universal" };

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

    private static void ValidateEnemies(Ctx ctx)
    {
        foreach (var e in ctx.Db.Enemies.All)
        {
            if (e.Life < 1) ctx.Error(e, "life must be >= 1.");
            if (e.Size.Length != 2 || e.Size.Any(v => v <= 0)) ctx.Error(e, "size must be [width, height] > 0.");
            if (e.KnockbackResist is < 0 or > 1) ctx.Error(e, "knockbackResist must be 0–1.");
            ctx.Spec(e, SpecKind.Brain, e.Brain, "brain");
            ctx.Ref(e, ctx.Db.LootTables, e.Loot, "loot");
            ctx.Color(e, e.Tint, "tint");
            ctx.Light(e, e.Light, "light");
            ctx.Asset(e, AssetPaths.CreatureModel(e), "model");
            if (e.Boss != null)
            {
                ctx.Ref(e, ctx.Db.Flags, e.Boss.Flag, "boss.flag");
                if (string.IsNullOrWhiteSpace(e.Boss.Title)) ctx.Error(e, "boss.title is required.");
            }
            foreach (var key in new[] { "minion", "servant" })
                if (e.Brain.Raw.TryGetProperty(key, out var refId) && refId.GetString() is { } id) ctx.Ref(e, ctx.Db.Enemies, id, $"brain.{key}");
            if (e.Brain.Raw.TryGetProperty("phase2Model", out var model) && model.GetString() is { } m)
                ctx.Asset(e, $"assets/models/creatures/{m}.glb", "brain.phase2Model");
        }
        foreach (var s in ctx.Db.SpawnRules.All)
        {
            ctx.Ref(s, ctx.Db.Enemies, s.Enemy, "enemy");
            if (s.Weight <= 0) ctx.Error(s, "weight must be > 0.");
            if (s.GroupMin < 1 || s.GroupMax < s.GroupMin) ctx.Error(s, "group must be [min>=1, max>=min].");
            ctx.Specs(s, SpecKind.Condition, s.Conditions, "conditions");
        }
    }

    private static void ValidateProjectilesAndBuffs(Ctx ctx)
    {
        foreach (var p in ctx.Db.Projectiles.All)
        {
            if (p.Lifetime < 1) ctx.Error(p, "lifetime must be >= 1.");
            if (p.Radius <= 0) ctx.Error(p, "radius must be > 0.");
            ctx.Light(p, p.Light, "light");
            ctx.Color(p, p.Color, "color");
            if (p.Model != null) ctx.Asset(p, AssetPaths.ProjectileModel(p), "model");
            ctx.Specs(p, SpecKind.ProjectileHit, p.OnHit, "onHit");
        }
        foreach (var b in ctx.Db.Buffs.All)
        {
            ctx.Color(b, b.Color, "color");
            ctx.Specs(b, SpecKind.Effect, b.Effects, "effects");
        }
        foreach (var p in ctx.Db.Prefixes.All)
        {
            if (p.Applies.Length == 0) ctx.Error(p, "prefix must apply to at least one group.");
            foreach (var a in p.Applies)
                if (!PrefixGroups.Contains(a)) ctx.Error(p, $"unknown prefix group '{a}'.");
        }
    }

    private static void ValidateLoot(Ctx ctx)
    {
        foreach (var t in ctx.Db.LootTables.All)
        {
            if (t.Entries.Length == 0 && t.Coins == null) ctx.Error(t, "loot table is empty.");
            if (t.Coins is { } coins && (coins.Length != 2 || coins[0] < 0 || coins[1] < coins[0]))
                ctx.Error(t, "coins must be [min, max].");
            foreach (var e in t.Entries)
            {
                if ((e.Item == null) == (e.Table == null)) ctx.Error(t, "each entry needs exactly one of item/table.");
                ctx.Ref(t, ctx.Db.Items, e.Item, "entries.item");
                ctx.Ref(t, ctx.Db.LootTables, e.Table, "entries.table");
                if (e.Min < 0 || e.Max < e.Min) ctx.Error(t, "entry min/max invalid.");
                if (e.Chance is < 0 or > 1) ctx.Error(t, "entry chance must be 0–1.");
                ctx.Specs(t, SpecKind.Condition, e.Conditions, "entries.conditions");
            }
        }
        DetectCycles(ctx, ctx.Db.LootTables.All, t => t.Entries.Where(e => e.Table != null).Select(e => e.Table!), "loot table");
    }

    private static void ValidateFlags(Ctx ctx)
    {
        foreach (var f in ctx.Db.Flags.All)
            foreach (var r in f.Requires)
                ctx.Ref(f, ctx.Db.Flags, r, "requires");
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
