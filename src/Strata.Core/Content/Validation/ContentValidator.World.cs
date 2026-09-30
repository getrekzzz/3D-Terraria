using Strata.Core.Content.Defs;
using Strata.Core.Content.Specs;

namespace Strata.Core.Content.Validation;

public static partial class ContentValidator
{
    private static readonly HashSet<string> PrefixGroups = new() { "melee", "ranged", "magic", "summon", "tool", "accessory", "universal" };

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

    /// <summary>Pattern bosses name projectiles, minions and models that must exist.</summary>
    private static void ValidatePattern(Ctx ctx, EnemyDef e)
    {
        Specs.PatternParams p;
        try { p = e.Brain.Parse<Specs.PatternParams>(); }
        catch (Exception) { return; } // reported by the spec check
        foreach (var phase in p.Phases)
        {
            if (phase.Model != null) ctx.Asset(e, $"assets/models/creatures/{phase.Model}.glb", "brain.phases.model");
            foreach (var guard in (phase.ShieldedBy ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                if (!ctx.Db.Enemies.Contains(guard)) ctx.Error(e, $"shieldedBy names unknown enemy '{guard}'.");
            if (phase.Summon is { } s)
                foreach (var id in s.Pool.Length > 0 ? s.Pool : new[] { s.Enemy })
                    if (!ctx.Db.Enemies.Contains(id)) ctx.Error(e, $"summons unknown enemy '{id}'.");
            foreach (var v in phase.Moves.Select(m => m.Volley).Append(phase.Volley))
                if (v != null && !ctx.Db.Projectiles.Contains(v.Projectile)) ctx.Error(e, $"volley fires unknown projectile '{v.Projectile}'.");
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
            if (e.Brain.Type == Specs.BrainSpecs.Pattern) ValidatePattern(ctx, e);
            ctx.Ref(e, ctx.Db.LootTables, e.Loot, "loot");
            ctx.Ref(e, ctx.Db.Items, e.CatchItem, "catchItem");
            ctx.Color(e, e.Tint, "tint");
            ctx.Color(e, e.TetherColor, "tetherColor");
            if (e.Facing is not ("yaw" or "look" or "velocity")) ctx.Error(e, $"facing '{e.Facing}' must be yaw, look or velocity.");
            ctx.Light(e, e.Light, "light");
            ctx.Asset(e, AssetPaths.CreatureModel(e), "model");
            if (e.Boss is { } boss)
            {
                ctx.Ref(e, ctx.Db.Flags, boss.Flag, "boss.flag");
                if (string.IsNullOrWhiteSpace(boss.Title) && !boss.Minor) ctx.Error(e, "boss.title is required.");
                foreach (var (part, field) in new[] { (boss.SegmentModel, "segmentModel"), (boss.TailModel, "tailModel") })
                    if (part != null) ctx.Asset(e, $"assets/models/creatures/{part}.glb", $"boss.{field}");
                for (int i = 0; i < boss.SegmentModels.Length; i++)
                    ctx.Asset(e, $"assets/models/creatures/{boss.SegmentModels[i]}.glb", $"boss.segmentModels[{i}]");
                if (boss.SegmentModels.Length > boss.Segments) ctx.Error(e, "boss.segmentModels lists more models than boss.segments.");
                if (boss.BarrierModel != null) ctx.Asset(e, $"assets/models/creatures/{boss.BarrierModel}.glb", "boss.barrierModel");
                if (boss.ArmModel != null) ctx.Asset(e, $"assets/models/creatures/{boss.ArmModel}.glb", "boss.armModel");
                if (boss.BarrierTile <= 0) ctx.Error(e, "boss.barrierTile must be > 0.");
                if (boss.DayEnrage is { Damage: <= 0 } or { Speed: <= 0 }) ctx.Error(e, "boss.dayEnrage multipliers must be > 0.");
                foreach (var part in boss.Parts)
                {
                    ctx.Ref(e, ctx.Db.Enemies, part.Enemy, "boss.parts");
                    if (part.Offset.Length != 3) ctx.Error(e, "boss.parts offsets are [right, up, forward].");
                }
                if (boss.Ritual is { } ritual)
                {
                    ctx.Ref(e, ctx.Db.Flags, ritual.Requires, "boss.ritual.requires");
                    ctx.Ref(e, ctx.Db.Enemies, ritual.Worshipper, "boss.ritual.worshipper");
                    ctx.Ref(e, ctx.Db.Enemies, ritual.Altar, "boss.ritual.altar");
                    ctx.Ref(e, ctx.Db.Enemies, ritual.Guard, "boss.ritual.guard");
                    if (!WorldGen.Plan.LandmarkIds.All.Contains(ritual.Landmark)) ctx.Error(e, $"boss.ritual.landmark '{ritual.Landmark}' is not a planned landmark.");
                }
                foreach (var companionId in boss.Companions)
                {
                    ctx.Ref(e, ctx.Db.Enemies, companionId, "boss.companions");
                    if (!ctx.Db.Enemies.TryGet(companionId, out var companion)) continue;
                    if (companion.Boss is not { } cb || cb.Flag != boss.Flag || boss.Flag == null)
                        ctx.Error(e, $"boss.companions: '{companionId}' must be a boss with the same flag ('{boss.Flag}').");
                    else if (cb.Companions.Length > 0)
                        ctx.Error(e, $"boss.companions: '{companionId}' may not bring companions of its own.");
                }
            }
            if (e.Brain.Type is BrainSpecs.CryptWarden or BrainSpecs.Mawwall && e.Boss is not { Segments: >= 2 })
                ctx.Error(e, $"the {e.Brain.Type} brain needs a boss block with segments >= 2 (its hands or eyes).");
            if (e.Brain.Type == BrainSpecs.Mawwall && e.Brain.Raw.TryGetProperty("minion", out var minion)) ctx.Ref(e, ctx.Db.Enemies, minion.GetString(), "brain.minion");
            if (e.Ranged is { FromParts: true } && e.Boss is not { Segments: > 0 }) ctx.Error(e, "ranged.fromParts needs body parts (boss.segments).");
            foreach (var key in new[] { "minion", "servant" })
                if (e.Brain.Raw.TryGetProperty(key, out var refId) && refId.GetString() is { } id) ctx.Ref(e, ctx.Db.Enemies, id, $"brain.{key}");
            if (e.Brain.Raw.TryGetProperty("phase2Model", out var model) && model.GetString() is { } m)
                ctx.Asset(e, $"assets/models/creatures/{m}.glb", "brain.phase2Model");
            foreach (var (r, field) in new[] { (e.Ranged, "ranged"), (e.Ranged?.Enraged, "ranged.enraged") })
            {
                if (r == null) continue;
                if (ctx.Db.Projectiles.TryGet(r.Projectile, out var proj) && proj.Friendly) ctx.Error(e, $"{field}.projectile '{r.Projectile}' must be hostile (friendly: false).");
                ctx.Ref(e, ctx.Db.Projectiles, r.Projectile, $"{field}.projectile");
                if (r.Interval < 1 || r.Count < 1 || r.Speed <= 0 || r.Range <= 0) ctx.Error(e, $"{field} needs interval, count, speed and range > 0.");
                if (r.Parts.Any(i => i < 0 || i >= (e.Boss?.Segments ?? 0))) ctx.Error(e, $"{field}.parts must index body parts (0..boss.segments-1).");
                if (r.Enraged?.Enraged != null) ctx.Error(e, $"{field}.enraged may not nest another enraged attack.");
            }
            ctx.Specs(e, SpecKind.Condition, e.DespawnWhen, "despawnWhen");
            if (e.Shield is { } shield && (shield.Kills < 1 || string.IsNullOrEmpty(shield.KillTag))) ctx.Error(e, "shield needs killTag and kills >= 1.");
            ctx.Specs(e, SpecKind.CreatureEffect, e.OnContact, "onContact");
            ctx.Specs(e, SpecKind.CreatureEffect, e.OnDeath, "onDeath");
            foreach (var fx in e.OnContact.Concat(e.OnDeath))
            {
                string? Str(string name) => fx.Raw.TryGetProperty(name, out var v) ? v.GetString() : null;
                ctx.Ref(e, ctx.Db.Buffs, fx.Type == CreatureEffectSpecs.ApplyBuff ? Str("buff") : null, "effect.buff");
                ctx.Ref(e, ctx.Db.Liquids, fx.Type == CreatureEffectSpecs.PourLiquid ? Str("liquid") : null, "effect.liquid");
                ctx.Ref(e, ctx.Db.Enemies, fx.Type == CreatureEffectSpecs.SpawnEnemies ? Str("enemy") : null, "effect.enemy");
            }
        }
        foreach (var s in ctx.Db.SpawnRules.All)
        {
            ctx.Ref(s, ctx.Db.Enemies, s.Enemy, "enemy");
            if (s.Weight <= 0) ctx.Error(s, "weight must be > 0.");
            if (s.GroupMin < 1 || s.GroupMax < s.GroupMin) ctx.Error(s, "group must be [min>=1, max>=min].");
            if (s.MaxAlive < 0) ctx.Error(s, "maxAlive must be >= 0.");
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
