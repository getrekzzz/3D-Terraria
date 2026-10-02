using Strata.Core.Content.Defs;
using Strata.Core.Content.Specs;

namespace Strata.Core.Content.Validation;

public static partial class ContentValidator
{
    private static readonly HashSet<string> PrefixGroups = new() { "universal", "common", "melee", "ranged", "magic", "accessory" };

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
            foreach (var h in p.OnHit.Where(h => h.Type == ProjectileHitSpecs.SpawnProjectiles))
                if (h.Parse<SpawnProjectilesParams>().Projectile is { Length: > 0 } child && !ctx.Db.Projectiles.Contains(child))
                    ctx.Error(p, $"spawnProjectiles releases unknown projectile '{child}'.");
            if (p.Latch && p.LatchInterval < 1) ctx.Error(p, "latchInterval must be >= 1.");
            if (p.Minion is { Shoot: { } shot } && !ctx.Db.Projectiles.Contains(shot)) ctx.Error(p, $"minion shoots unknown projectile '{shot}'.");
            if (p.Minion is { Speed: < 0 } or { Range: <= 0 }) ctx.Error(p, "minion speed must be >= 0 (0 = a sentry) and range > 0.");
            if (p.Minion is { Speed: 0, Shoot: null }) ctx.Error(p, "a sentry (minion speed 0) needs something to shoot.");
            if (p.Pet is { } pet)
            {
                if (p.Friendly || p.Contact || p.Hostile) ctx.Error(p, "pets are harmless: set friendly and contact to false.");
                if (p.Minion != null) ctx.Error(p, "a projectile cannot be both a pet and a minion.");
                if (pet.Speed <= 0 || pet.Follow < 0 || pet.Hover < 0) ctx.Error(p, "pet speed must be > 0, follow and hover >= 0.");
            }
            if (p.Fuse && !p.OnHit.Any(h => h.Type == Specs.ProjectileHitSpecs.Explode)) ctx.Warn(p, "fuse without an explode effect does nothing.");
        }
        // Pet and light-pet items summon a pet projectile from their misc slot.
        foreach (var i in ctx.Db.Items.All)
        {
            if (i.EquipSlot is not (EquipSlot.Pet or EquipSlot.LightPet)) continue;
            if (i.Projectile == null || !ctx.Db.Projectiles.TryGet(i.Projectile, out var pet) || pet.Pet == null)
                ctx.Error(i, "pet items need a projectile with 'pet' movement.");
            else if (i.EquipSlot == EquipSlot.LightPet && pet.Light is not { Length: 3 })
                ctx.Error(i, $"light pet '{pet.Id}' needs a light.");
        }
        foreach (var b in ctx.Db.Buffs.All)
        {
            ctx.Color(b, b.Color, "color");
            ctx.Specs(b, SpecKind.Effect, b.Effects, "effects");
            ctx.Asset(b, AssetPaths.BuffIcon(b), "icon");
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
