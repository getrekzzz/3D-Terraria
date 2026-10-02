using Strata.Core.Content;
using Strata.Core.Content.Specs;
using Strata.Core.World;

namespace Strata.Core.Conditions;

/// <summary>Compiles condition specs from content into <see cref="ICondition"/> objects.</summary>
public static partial class ConditionCompiler
{
    private static readonly Dictionary<string, Func<TypedSpec, ICondition>> Factories = Build();

    private static Dictionary<string, Func<TypedSpec, ICondition>> Build()
    {
        var f = new Dictionary<string, Func<TypedSpec, ICondition>>(StringComparer.Ordinal);
        AddWorld(f);
        AddTown(f);
        AddComposites(f);
        return f;
    }

    /// <summary>Place, time, world progress and combat conditions.</summary>
    private static void AddWorld(Dictionary<string, Func<TypedSpec, ICondition>> f)
    {
        f["biome"] = s =>
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
        };
        f["layer"] = s =>
        {
            var p = s.Parse<LayerConditionParams>();
            var layers = p.Any.Select(l => Enum.Parse<WorldLayer>(l, ignoreCase: true)).ToArray();
            return new DelegateCondition(c => Array.IndexOf(layers, c.Layer) >= 0);
        };
        f["time"] = s =>
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
        };
        f["flag"] = s =>
        {
            var p = s.Parse<FlagConditionParams>();
            return new DelegateCondition(c => c.Flags.Contains(p.Id) == p.Set);
        };
        f["event"] = s =>
        {
            var p = s.Parse<EventConditionParams>();
            if (p.MinKills <= 0) return new DelegateCondition(c => c.Events.Contains(p.Id) == p.Active);
            return new DelegateCondition(c => c.Events.Contains(p.Id) && c.EventKills.TryGetValue(p.Id, out int k) && k >= p.MinKills);
        };
        f["depth"] = s =>
        {
            var p = s.Parse<RangeConditionParams>();
            return new DelegateCondition(c => c.Depth >= p.Min && c.Depth <= p.Max);
        };
        f["light"] = s =>
        {
            var p = s.Parse<RangeConditionParams>();
            return new DelegateCondition(c => c.Light >= p.Min && c.Light <= p.Max);
        };
        f["moon"] = s =>
        {
            var p = s.Parse<MoonConditionParams>();
            return new DelegateCondition(c => Array.IndexOf(p.Phases, c.MoonPhase) >= 0);
        };
        f["nearLiquid"] = s =>
        {
            var p = s.Parse<NearLiquidParams>();
            return new DelegateCondition(c => c.NearbyLiquids.Contains(p.Liquid));
        };
        f["chance"] = s =>
        {
            var p = s.Parse<ChanceConditionParams>();
            return new DelegateCondition(c => c.Random.NextFloat() < p.P);
        };
        f["wearing"] = s =>
        {
            var p = s.Parse<WearingConditionParams>();
            return new DelegateCondition(c => c.Wearing?.Invoke(p.Item) ?? false);
        };
        f["difficulty"] = s =>
        {
            var p = s.Parse<RangeConditionParams>();
            return new DelegateCondition(c => c.Difficulty >= p.Min && c.Difficulty <= p.Max);
        };
        f["expert"] = s =>
        {
            var p = s.Parse<ExpertConditionParams>();
            return new DelegateCondition(c => (c.Difficulty is 1 or 2) == p.Is);
        };
        f["defense"] = s =>
        {
            var p = s.Parse<RangeConditionParams>();
            return new DelegateCondition(c => c.PlayerDefense >= p.Min && c.PlayerDefense <= p.Max);
        };
        f["evil"] = s =>
        {
            var p = s.Parse<EvilConditionParams>();
            return new DelegateCondition(c => string.Equals(c.Evil, p.Is, StringComparison.OrdinalIgnoreCase));
        };
        f["fishingPower"] = s =>
        {
            var p = s.Parse<RangeConditionParams>();
            return new DelegateCondition(c => c.FishingPower >= p.Min && c.FishingPower <= p.Max);
        };
        f["worldRing"] = s =>
        {
            var p = s.Parse<RangeConditionParams>();
            return new DelegateCondition(c => c.CentreDistance >= p.Min && c.CentreDistance <= p.Max);
        };
    }

    private static void AddComposites(Dictionary<string, Func<TypedSpec, ICondition>> f)
    {
        f["all"] = s => new AllCondition(s.Parse<CompositeConditionParams>().Of.Select(Compile).ToArray());
        f["any"] = s => new AnyCondition(s.Parse<CompositeConditionParams>().Of.Select(Compile).ToArray());
        f["not"] = s => new NotCondition(new AllCondition(s.Parse<CompositeConditionParams>().Of.Select(Compile).ToArray()));
    }

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
        RegisterTownSpecs(c);
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
