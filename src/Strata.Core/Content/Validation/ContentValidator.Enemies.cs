using Strata.Core.Content.Defs;
using Strata.Core.Content.Specs;

namespace Strata.Core.Content.Validation;

public static partial class ContentValidator
{
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
            if (phase.Aura is { } aura && !ctx.Db.Buffs.Contains(aura.Buff)) ctx.Error(e, $"aura pulses unknown buff '{aura.Buff}'.");
            if (phase.Leash is { } leash && !ctx.Db.Enemies.Contains(leash.Kind)) ctx.Error(e, $"leash names unknown enemy '{leash.Kind}'.");
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
            ctx.Ref(e, ctx.Db.Enemies, e.BestiaryAs, "bestiaryAs");
            if (e.BestiaryAs != null && ctx.Db.Enemies.TryGet(e.BestiaryAs, out var parent) && parent.BestiaryAs != null)
                ctx.Error(e, $"bestiaryAs '{e.BestiaryAs}' itself credits another entry (one level only).");
            ctx.Color(e, e.Tint, "tint");
            ctx.Color(e, e.TetherColor, "tetherColor");
            if (e.Facing is not ("yaw" or "look" or "velocity")) ctx.Error(e, $"facing '{e.Facing}' must be yaw, look or velocity.");
            ctx.Light(e, e.Light, "light");
            ctx.Asset(e, AssetPaths.CreatureModel(e), "model");
            ctx.Sound(e, e.HitSound, "hitSound");
            ctx.Sound(e, e.DeathSound, "deathSound");
            if (e.Boss is { Minor: false }) ctx.Asset(e, AssetPaths.BossIcon(e), "boss head icon");
            if (e.Disguise is { } disguise)
            {
                if (string.IsNullOrWhiteSpace(disguise.Model)) ctx.Error(e, "disguise.model is required.");
                else ctx.Asset(e, $"assets/models/creatures/{disguise.Model}.glb", "disguise.model");
                if (disguise.WakeRange < 0 || disguise.Lunge < 0) ctx.Error(e, "disguise.wakeRange and disguise.lunge must be >= 0.");
                if (disguise.WakeRange == 0 && !disguise.WakeOnInteract) ctx.Error(e, "a disguise that ignores interaction needs a wakeRange (hits alone rarely find it).");
            }
            if (e.Boss is { } boss)
            {
                ctx.Ref(e, ctx.Db.Flags, boss.Flag, "boss.flag");
                if (string.IsNullOrWhiteSpace(boss.Title) && !boss.Minor) ctx.Error(e, "boss.title is required.");
                foreach (var (part, field) in new[] { (boss.SegmentModel, "segmentModel"), (boss.TailModel, "tailModel") })
                    if (part != null) ctx.Asset(e, $"assets/models/creatures/{part}.glb", $"boss.{field}");
                for (int i = 0; i < boss.SegmentPattern.Length; i++)
                    ctx.Asset(e, $"assets/models/creatures/{boss.SegmentPattern[i]}.glb", $"boss.segmentPattern[{i}]");
                for (int i = 0; i < boss.SegmentModels.Length; i++)
                    ctx.Asset(e, $"assets/models/creatures/{boss.SegmentModels[i]}.glb", $"boss.segmentModels[{i}]");
                if (boss.SegmentModels.Length > boss.Segments) ctx.Error(e, "boss.segmentModels lists more models than boss.segments.");
                if (boss.BarrierModel != null) ctx.Asset(e, $"assets/models/creatures/{boss.BarrierModel}.glb", "boss.barrierModel");
                if (boss.ArmModel != null) ctx.Asset(e, $"assets/models/creatures/{boss.ArmModel}.glb", "boss.armModel");
                if (boss.BarrierTile <= 0) ctx.Error(e, "boss.barrierTile must be > 0.");
                if (boss.Intro is { } intro)
                {
                    if (boss.Minor) ctx.Error(e, "boss.intro: a minor boss gets no title card.");
                    if (string.IsNullOrWhiteSpace(intro.Subtitle)) ctx.Error(e, "boss.intro.subtitle is required.");
                    ctx.Color(e, intro.Color, "boss.intro.color");
                    if (intro.Framing is < 0f or > Presentation.BossIntroModel.MaxFramingDistance)
                        ctx.Error(e, $"boss.intro.framing must be 0-{Presentation.BossIntroModel.MaxFramingDistance} m.");
                    if (intro.Letterbox is < 0f or > Presentation.BossIntroModel.MaxLetterbox)
                        ctx.Error(e, $"boss.intro.letterbox must be 0-{Presentation.BossIntroModel.MaxLetterbox} s.");
                }
                if (boss.Enrage is { } rage)
                {
                    ctx.Specs(e, SpecKind.Condition, rage.When, "boss.enrage.when");
                    if (rage.Damage <= 0 || rage.Speed <= 0) ctx.Error(e, "boss.enrage multipliers must be > 0.");
                    if (rage.SetDamage < 0 || rage.SetDefense < 0) ctx.Error(e, "boss.enrage setDamage and setDefense must be >= 0.");
                }
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
            if (e.Brain.Type == BrainSpecs.Mawwall && e.Boss is not { Segments: >= 2 })
                ctx.Error(e, $"the {e.Brain.Type} brain needs a boss block with segments >= 2 (its eyes).");
            if (e.Brain.Type == BrainSpecs.CryptWarden && e.Boss is not ({ Segments: >= 2 } or { Parts.Length: >= 2 }))
                ctx.Error(e, $"the {e.Brain.Type} brain needs a boss block with segments >= 2 or parts >= 2 (its hands).");
            if (e.Brain.Type == BrainSpecs.Hand && !ctx.Db.Enemies.All.Any(b => b.Boss is { } bb && bb.Parts.Any(part => part.Enemy == e.Id)))
                ctx.Warn(e, $"the {e.Brain.Type} brain only works as a boss part, but no boss lists it in boss.parts.");
            if (e.Brain.Type == BrainSpecs.Mawwall && e.Brain.Raw.TryGetProperty("minion", out var minion)) ctx.Ref(e, ctx.Db.Enemies, minion.GetString(), "brain.minion");
            if (e.Ranged is { FromParts: true } && e.Boss is not { Segments: > 0 }) ctx.Error(e, "ranged.fromParts needs body parts (boss.segments).");
            foreach (var key in new[] { "minion", "servant" })
                if (e.Brain.Raw.TryGetProperty(key, out var refId) && refId.GetString() is { } id) ctx.Ref(e, ctx.Db.Enemies, id, $"brain.{key}");
            if (e.Brain.Type == BrainSpecs.CryptWarden && e.Brain.Raw.TryGetProperty("skull", out var skull) && skull.GetString() is { } skullId)
            {
                ctx.Ref(e, ctx.Db.Projectiles, skullId, "brain.skull");
                if (ctx.Db.Projectiles.TryGet(skullId, out var sp) && sp.Friendly) ctx.Error(e, $"brain.skull '{skullId}' must be hostile (friendly: false).");
            }
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
}
