using Strata.Core.Content;
using Strata.Core.Content.Specs;
using Strata.Core.World;

namespace Strata.Core.Conditions;

/// <summary>Compiles condition specs from content into <see cref="ICondition"/> objects.</summary>
public static class ConditionCompiler
{
    private static readonly Dictionary<string, Func<TypedSpec, ICondition>> Factories = new(StringComparer.Ordinal)
    {
        ["biome"] = s =>
        {
            var p = s.Parse<BiomeConditionParams>();
            return new DelegateCondition(c =>
            {
                foreach (var n in p.None)
                    if (c.HasBiome(n)) return false;
                if (p.Any.Length == 0) return true;
                foreach (var a in p.Any)
                    if (c.HasBiome(a)) return true;
                return false;
            });
        },
        ["layer"] = s =>
        {
            var p = s.Parse<LayerConditionParams>();
            var layers = p.Any.Select(l => Enum.Parse<WorldLayer>(l, ignoreCase: true)).ToArray();
            return new DelegateCondition(c => Array.IndexOf(layers, c.Layer) >= 0);
        },
        ["time"] = s =>
        {
            var p = s.Parse<TimeConditionParams>();
            return p.Is switch
            {
                "day" => new DelegateCondition(c => c.IsDay),
                "night" => new DelegateCondition(c => !c.IsDay),
                _ => new DelegateCondition(c => p.FromHour <= p.ToHour
                    ? c.Hour >= p.FromHour && c.Hour < p.ToHour
                    : c.Hour >= p.FromHour || c.Hour < p.ToHour),
            };
        },
        ["flag"] = s =>
        {
            var p = s.Parse<FlagConditionParams>();
            return new DelegateCondition(c => c.Flags.Contains(p.Id) == p.Set);
        },
        ["event"] = s =>
        {
            var p = s.Parse<EventConditionParams>();
            if (p.MinKills <= 0) return new DelegateCondition(c => c.Events.Contains(p.Id) == p.Active);
            return new DelegateCondition(c => c.Events.Contains(p.Id) && c.EventKills.TryGetValue(p.Id, out int k) && k >= p.MinKills);
        },
        ["depth"] = s =>
        {
            var p = s.Parse<RangeConditionParams>();
            return new DelegateCondition(c => c.Depth >= p.Min && c.Depth <= p.Max);
        },
        ["light"] = s =>
        {
            var p = s.Parse<RangeConditionParams>();
            return new DelegateCondition(c => c.Light >= p.Min && c.Light <= p.Max);
        },
        ["moon"] = s =>
        {
            var p = s.Parse<MoonConditionParams>();
            return new DelegateCondition(c => Array.IndexOf(p.Phases, c.MoonPhase) >= 0);
        },
        ["nearLiquid"] = s =>
        {
            var p = s.Parse<NearLiquidParams>();
            return new DelegateCondition(c => c.NearbyLiquids.Contains(p.Liquid));
        },
        ["chance"] = s =>
        {
            var p = s.Parse<ChanceConditionParams>();
            return new DelegateCondition(c => c.Random.NextFloat() < p.P);
        },
        ["coins"] = s =>
        {
            var p = s.Parse<CoinsConditionParams>();
            return new DelegateCondition(c => c.PlayerCoins >= p.Min);
        },
        ["maxLife"] = s =>
        {
            var p = s.Parse<RangeConditionParams>();
            return new DelegateCondition(c => c.PlayerMaxLife >= p.Min && c.PlayerMaxLife <= p.Max);
        },
        ["resident"] = s =>
        {
            var p = s.Parse<ResidentConditionParams>();
            return new DelegateCondition(c => c.Residents.Contains(p.Id) == p.Present);
        },
        ["residents"] = s =>
        {
            var p = s.Parse<RangeConditionParams>();
            return new DelegateCondition(c => c.ResidentCount >= p.Min && c.ResidentCount <= p.Max);
        },
        ["hasItem"] = s =>
        {
            var p = s.Parse<HasItemConditionParams>();
            string key = p.Item ?? "#" + p.Tag;
            return new DelegateCondition(c => (c.ItemCount?.Invoke(key) ?? 0) >= p.Count);
        },
        ["wearing"] = s =>
        {
            var p = s.Parse<WearingConditionParams>();
            return new DelegateCondition(c => c.Wearing?.Invoke(p.Item) ?? false);
        },
        ["difficulty"] = s =>
        {
            var p = s.Parse<RangeConditionParams>();
            return new DelegateCondition(c => c.Difficulty >= p.Min && c.Difficulty <= p.Max);
        },
        ["expert"] = s =>
        {
            var p = s.Parse<ExpertConditionParams>();
            return new DelegateCondition(c => (c.Difficulty is 1 or 2) == p.Is);
        },
        ["defense"] = s =>
        {
            var p = s.Parse<RangeConditionParams>();
            return new DelegateCondition(c => c.PlayerDefense >= p.Min && c.PlayerDefense <= p.Max);
        },
        ["evil"] = s =>
        {
            var p = s.Parse<EvilConditionParams>();
            return new DelegateCondition(c => string.Equals(c.Evil, p.Is, StringComparison.OrdinalIgnoreCase));
        },
        ["fishingPower"] = s =>
        {
            var p = s.Parse<RangeConditionParams>();
            return new DelegateCondition(c => c.FishingPower >= p.Min && c.FishingPower <= p.Max);
        },
        ["worldRing"] = s =>
        {
            var p = s.Parse<RangeConditionParams>();
            return new DelegateCondition(c => c.CentreDistance >= p.Min && c.CentreDistance <= p.Max);
        },
        ["all"] = s => new AllCondition(s.Parse<CompositeConditionParams>().Of.Select(Compile).ToArray()),
        ["any"] = s => new AnyCondition(s.Parse<CompositeConditionParams>().Of.Select(Compile).ToArray()),
        ["not"] = s => new NotCondition(new AllCondition(s.Parse<CompositeConditionParams>().Of.Select(Compile).ToArray())),
    };

    public static IEnumerable<string> Types => Factories.Keys;

    public static ICondition Compile(TypedSpec spec)
    {
        if (!Factories.TryGetValue(spec.Type, out var factory))
            throw new InvalidOperationException($"Unknown condition type '{spec.Type}'.");
        return factory(spec);
    }

    /// <summary>Compiles a list into one conjunction (empty list = always true).</summary>
    public static ICondition CompileAll(IReadOnlyList<TypedSpec> specs) =>
        specs.Count == 0 ? TrueCondition.Instance
        : specs.Count == 1 ? Compile(specs[0])
        : new AllCondition(specs.Select(Compile).ToArray());

    internal static void RegisterSpecs(SpecCatalog c)
    {
        c.Register<BiomeConditionParams>(SpecKind.Condition, "biome");
        c.Register<LayerConditionParams>(SpecKind.Condition, "layer");
        c.Register<TimeConditionParams>(SpecKind.Condition, "time");
        c.Register<FlagConditionParams>(SpecKind.Condition, "flag");
        c.Register<EventConditionParams>(SpecKind.Condition, "event");
        c.Register<RangeConditionParams>(SpecKind.Condition, "depth");
        c.Register<RangeConditionParams>(SpecKind.Condition, "light");
        c.Register<MoonConditionParams>(SpecKind.Condition, "moon");
        c.Register<NearLiquidParams>(SpecKind.Condition, "nearLiquid");
        c.Register<ChanceConditionParams>(SpecKind.Condition, "chance");
        c.Register<CoinsConditionParams>(SpecKind.Condition, "coins");
        c.Register<RangeConditionParams>(SpecKind.Condition, "maxLife");
        c.Register<ResidentConditionParams>(SpecKind.Condition, "resident");
        c.Register<RangeConditionParams>(SpecKind.Condition, "residents");
        c.Register<HasItemConditionParams>(SpecKind.Condition, "hasItem");
        c.Register<WearingConditionParams>(SpecKind.Condition, "wearing");
        c.Register<RangeConditionParams>(SpecKind.Condition, "difficulty");
        c.Register<RangeConditionParams>(SpecKind.Condition, "fishingPower");
        c.Register<ExpertConditionParams>(SpecKind.Condition, "expert");
        c.Register<RangeConditionParams>(SpecKind.Condition, "defense");
        c.Register<EvilConditionParams>(SpecKind.Condition, "evil");
        c.Register<RangeConditionParams>(SpecKind.Condition, "worldRing");
        c.Register<CompositeConditionParams>(SpecKind.Condition, "all");
        c.Register<CompositeConditionParams>(SpecKind.Condition, "any");
        c.Register<CompositeConditionParams>(SpecKind.Condition, "not");
    }
}
