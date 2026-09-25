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
    public HashSet<string> Immunities { get; } = new(StringComparer.Ordinal);
    public int DamagePerSecond { get; set; }

    /// <summary>Brightest emitted light [r,g,b] (0–15), or null.</summary>
    public int[]? Light { get; set; }

    public void Clear()
    {
        Stats.Clear();
        ExtraJumps.Clear();
        NoFallDamage = false;
        Immunities.Clear();
        DamagePerSecond = 0;
        Light = null;
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
            case EffectSpecs.EmitLight:
                var c = Parsed<EmitLightParams>(spec).Color;
                if (Light == null || c.Sum() > Light.Sum()) Light = c;
                break;
            case EffectSpecs.NoFallDamage:
                NoFallDamage = true;
                break;
            default:
                throw new InvalidOperationException($"Unhandled effect type '{spec.Type}'.");
        }
    }

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
