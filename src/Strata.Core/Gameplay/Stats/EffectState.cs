using Strata.Core.Content;
using Strata.Core.Content.Specs;

namespace Strata.Core.Gameplay.Stats;

/// <summary>The combined result of every active effect (equipment, set bonuses, buffs) on an entity.</summary>
public sealed class EffectState
{
    public StatSheet Stats { get; } = new();

    /// <summary>Heights (cells) of extra mid-air jumps granted (one per effect).</summary>
    public List<float> ExtraJumps { get; } = new();

    public bool NoFallDamage { get; set; }

    /// <summary>Breathes under liquid (gills).</summary>
    public bool WaterBreathing { get; set; }

    /// <summary>Cannot use held items (hexed).</summary>
    public bool NoItemUse { get; set; }

    /// <summary>Sinks through blocks until clear of them (Shimmer).</summary>
    public bool Phasing { get; set; }

    /// <summary>Paints blocks as they are placed (Paint Sprayer).</summary>
    public bool AutoPaint { get; set; }

    /// <summary>Movement keys are reversed (Confused); <see cref="InvertForward"/> also swaps forward and back.</summary>
    public bool InvertControls { get; set; }

    public bool InvertForward { get; set; }

    public HashSet<string> Immunities { get; } = new(StringComparer.Ordinal);

    /// <summary>Liquids whose contact damage is ignored.</summary>
    public HashSet<string> LiquidImmunities { get; } = new(StringComparer.Ordinal);
    public int DamagePerSecond { get; set; }

    /// <summary>Brightest emitted light [r,g,b] (0–15), or null.</summary>
    public int[]? Light { get; set; }

    /// <summary>The best wings (glide) and rocket boots equipped, if any.</summary>
    public Content.Specs.FlightParams? Wings { get; set; }

    public Content.Specs.FlightParams? RocketBoots { get; set; }

    /// <summary>The fastest dash equipped, if any.</summary>
    public Content.Specs.DashParams? Dash { get; set; }

    /// <summary>Creature tags whose contact no longer hurts (Royal Gel).</summary>
    public HashSet<string> Pacified { get; } = new(StringComparer.Ordinal);

    /// <summary>Debuffs melee hits may inflict.</summary>
    public List<Content.Specs.InflictParams> Inflicts { get; } = new();

    /// <summary>HUD readouts the wearer has (depth, position, time).</summary>
    public HashSet<string> Info { get; } = new(StringComparer.Ordinal);

    /// <summary>Projectiles thrown with weapon attacks (Bone Glove).</summary>
    public List<Content.Specs.AttackProjectileParams> AttackProjectiles { get; } = new();

    /// <summary>Extra damage minion hits deal to this creature (whip tags; the strongest tag counts).</summary>
    public int MinionTagDamage { get; set; }

    public void Clear()
    {
        Stats.Clear();
        ExtraJumps.Clear();
        NoFallDamage = false;
        WaterBreathing = false;
        NoItemUse = false;
        Phasing = false;
        AutoPaint = false;
        InvertControls = false;
        InvertForward = false;
        Immunities.Clear();
        LiquidImmunities.Clear();
        DamagePerSecond = 0;
        Light = null;
        Wings = null;
        RocketBoots = null;
        Dash = null;
        Pacified.Clear();
        Inflicts.Clear();
        Info.Clear();
        AttackProjectiles.Clear();
        MinionTagDamage = 0;
    }

    /// <summary>Applies one effect spec (validated at content load).</summary>
    public void Apply(TypedSpec spec)
    {
        switch (spec.Type)
        {
            case EffectSpecs.Stat:
                var s = Parsed<StatEffectParams>(spec);
                Stats.Add(s.Stat, s.Add, s.Mul);
                break;
            case EffectSpecs.ExtraJump:
                ExtraJumps.Add(Parsed<ExtraJumpParams>(spec).Height);
                break;
            case EffectSpecs.DamageOverTime:
                DamagePerSecond += Parsed<DamageOverTimeParams>(spec).PerSecond;
                break;
            case EffectSpecs.Immune:
                Immunities.Add(Parsed<ImmuneParams>(spec).Buff);
                break;
            case EffectSpecs.LiquidImmune:
                LiquidImmunities.Add(Parsed<LiquidImmuneParams>(spec).Liquid);
                break;
            case EffectSpecs.EmitLight:
                var c = Parsed<EmitLightParams>(spec).Color;
                if (Light == null || c.Sum() > Light.Sum()) Light = c;
                break;
            case EffectSpecs.NoFallDamage:
                NoFallDamage = true;
                break;
            case EffectSpecs.Phase:
                Phasing = true;
                break;
            case EffectSpecs.AutoPaint:
                AutoPaint = true;
                break;
            case EffectSpecs.NoItemUse:
                NoItemUse = true;
                break;
            case EffectSpecs.WaterBreathing:
                WaterBreathing = true;
                break;
            case EffectSpecs.Flight:
                var f = Parsed<FlightParams>(spec);
                if (f.Boots) { if (RocketBoots == null || f.Time > RocketBoots.Time) RocketBoots = f; }
                else if (Wings == null || f.Time > Wings.Time) Wings = f;
                break;
            case EffectSpecs.Dash:
                var d = Parsed<DashParams>(spec);
                if (Dash == null || d.Speed > Dash.Speed) Dash = d;
                break;
            case EffectSpecs.Inflict:
                Inflicts.Add(Parsed<InflictParams>(spec));
                break;
            case EffectSpecs.Info:
                foreach (var k in Parsed<InfoParams>(spec).Show) Info.Add(k);
                break;
            case EffectSpecs.InvertControls:
                InvertControls = true;
                InvertForward |= Parsed<InvertControlsParams>(spec).Forward;
                break;
            case EffectSpecs.AttackProjectile:
                AttackProjectiles.Add(Parsed<AttackProjectileParams>(spec));
                break;
            case EffectSpecs.MinionTag:
                MinionTagDamage = Math.Max(MinionTagDamage, Parsed<MinionTagParams>(spec).Damage);
                break;
            case EffectSpecs.Pacify:
                Pacified.Add(Parsed<PacifyParams>(spec).Tag);
                break;
            default:
                throw new InvalidOperationException($"Unhandled effect type '{spec.Type}'.");
        }
    }

    /// <summary>
    /// Movement input (x = strafe right, y = back) as the player means it: reversed while confused. The camera is not
    /// affected.
    /// </summary>
    public (float X, float Y) SteerInput(float x, float y) => !InvertControls ? (x, y) : (-x, InvertForward ? -y : y);

    private static readonly Dictionary<TypedSpec, object> Cache = new();

    private static T Parsed<T>(TypedSpec spec) where T : SpecParams
    {
        lock (Cache)
        {
            if (!Cache.TryGetValue(spec, out var p)) Cache[spec] = p = spec.Parse<T>();
            return (T)p;
        }
    }
}
