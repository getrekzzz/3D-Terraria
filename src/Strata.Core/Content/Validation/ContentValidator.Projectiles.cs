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
            if (p.Trail != null) ctx.Color(p, p.Trail, "trail");
            if (p.TrailWidth is <= 0 or > 2) ctx.Error(p, "trailWidth must be within (0, 2] m.");
            if (p.TrailTime is <= 0 or > 3) ctx.Error(p, "trailTime must be within (0, 3] s.");
            if (p.Tether is { } tether)
            {
                if (Array.IndexOf(TetherDef.Kinds, tether.Kind) < 0) ctx.Error(p, $"tether.kind must be one of {string.Join(", ", TetherDef.Kinds)}.");
                if (tether.Range <= 0 || tether.Speed <= 0 || tether.Lifetime < 0) ctx.Error(p, "tether range and speed must be > 0, lifetime >= 0.");
                ctx.Color(p, tether.String, "tether.string");
                if (p.Returns > 0) ctx.Error(p, "a tethered projectile comes back on its own: drop 'returns'.");
            }
            if (Array.IndexOf(ProjectileDef.SpinAxes, p.SpinAxis) < 0) ctx.Error(p, $"spinAxis must be one of {string.Join(", ", ProjectileDef.SpinAxes)}.");
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
            foreach (var h in p.OnHit.Where(h => h.Type == ProjectileHitSpecs.DropItem))
                if (h.Parse<DropItemParams>().Item is { Length: > 0 } item && !ctx.Db.Items.Contains(item))
                    ctx.Error(p, $"dropItem leaves unknown item '{item}'.");
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
            if (b.FoodTier is < 0 or > 3) ctx.Error(b, "foodTier must be 0 (not food) to 3 (Exquisitely Stuffed).");
            if (b.Visual is { } v) ValidateBuffVisual(ctx, b, v);
        }
        foreach (var p in ctx.Db.Prefixes.All)
        {
            if (p.Applies.Length == 0) ctx.Error(p, "prefix must apply to at least one group.");
            foreach (var a in p.Applies)
                if (!PrefixGroups.Contains(a)) ctx.Error(p, $"unknown prefix group '{a}'.");
        }
    }

    private static void ValidateBuffVisual(Ctx ctx, BuffDef b, BuffVisualDef v)
    {
        ctx.Color(b, v.Tint, "visual.tint");
        ctx.Color(b, v.Color, "visual.color");
        ctx.Light(b, v.Light, "visual.light");
        if (v.Particle != null && Array.IndexOf(BuffVisualDef.Particles, v.Particle) < 0)
            ctx.Error(b, $"visual.particle '{v.Particle}' must be one of {string.Join(", ", BuffVisualDef.Particles)}.");
        if (v.TintStrength is < 0f or > 1f) ctx.Error(b, "visual.tintStrength must be within 0–1.");
        if (v.Rate is < 0f or > 60f) ctx.Error(b, "visual.rate must be within 0–60 particles per second.");
        if (v.Rise is < -4f or > 4f) ctx.Error(b, "visual.rise must be within -4–4.");
        if (v.Tint == null && v.Particle == null && v.Light == null) ctx.Warn(b, "visual block shows nothing (no tint, particle or light).");
    }

    private static void ValidateLoot(Ctx ctx)
    {
        foreach (var t in ctx.Db.LootTables.All)
        {
            if (t.Entries.Length == 0 && t.Coins == null) ctx.Error(t, "loot table is empty.");
            if (t.Coins is { } coins && (coins.Length != 2 || coins[0] < 0 || coins[1] < coins[0]))
                ctx.Error(t, "coins must be [min, max].");
            if (t.DepthScale is { } scale && (scale.Coins < 0 || scale.Counts < 0)) ctx.Error(t, "depthScale coins and counts must be 0 or more.");
            foreach (var e in t.Entries)
            {
                if ((e.Item == null) == (e.Table == null)) ctx.Error(t, "each entry needs exactly one of item/table.");
                ctx.Ref(t, ctx.Db.Items, e.Item, "entries.item");
                ctx.Ref(t, ctx.Db.LootTables, e.Table, "entries.table");
                if (e.Min < 0 || e.Max < e.Min) ctx.Error(t, "entry min/max invalid.");
                if (e.Chance is < 0 or > 1) ctx.Error(t, "entry chance must be 0–1.");
                if (e.DepthBonus < 0) ctx.Error(t, "entry depthBonus must be 0 or more.");
                else if (t.Mode == LootMode.Each && e.Chance + e.DepthBonus > 1.0001f) ctx.Warn(t, "entry chance + depthBonus passes 1 (certain before the underworld).");
                ctx.Specs(t, SpecKind.Condition, e.Conditions, "entries.conditions");
            }
        }
        DetectCycles(ctx, ctx.Db.LootTables.All, t => t.Entries.Where(e => e.Table != null).Select(e => e.Table!), "loot table");
    }
}
