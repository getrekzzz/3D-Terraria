using Strata.Core.Content.Specs;

namespace Strata.Core.Content.Validation;

public static partial class ContentValidator
{
    private static readonly HashSet<string> PrefixGroups = new() { "melee", "ranged", "magic", "summon", "tool", "accessory", "universal" };

    private static void ValidateProjectilesAndBuffs(Ctx ctx)
    {
        foreach (var p in ctx.Db.Projectiles.All)
        {
            if (p.Lifetime < 1) ctx.Error(p, "lifetime must be >= 1.");
            if (p.Radius <= 0) ctx.Error(p, "radius must be > 0.");
            ctx.Light(p, p.Light, "light");
            ctx.Color(p, p.Color, "color");
            switch (p.Visual)
            {
                case "creature": ctx.Asset(p, $"assets/models/creatures/{p.Model ?? p.Id}.glb", "model"); break;
                case "item" when !ctx.Db.Items.Contains(p.Model ?? p.Id): ctx.Error(p, $"item visual needs an item '{p.Model ?? p.Id}'."); break;
                case "item": break;
                default:
                    if (p.Model != null) ctx.Asset(p, AssetPaths.ProjectileModel(p), "model");
                    break;
            }
            ctx.Specs(p, SpecKind.ProjectileHit, p.OnHit, "onHit");
            if (p.Minion is { Shoot: { } shot } && !ctx.Db.Projectiles.Contains(shot)) ctx.Error(p, $"minion shoots unknown projectile '{shot}'.");
            if (p.Minion is { Speed: < 0 } or { Range: <= 0 }) ctx.Error(p, "minion speed must be >= 0 (0 = a sentry) and range > 0.");
            if (p.Minion is { Speed: 0, Shoot: null }) ctx.Error(p, "a sentry (minion speed 0) needs something to shoot.");
            if (p.Fuse && !p.OnHit.Any(h => h.Type == Specs.ProjectileHitSpecs.Explode)) ctx.Warn(p, "fuse without an explode effect does nothing.");
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
}
