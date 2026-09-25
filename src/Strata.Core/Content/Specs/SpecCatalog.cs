using System.Text.Json;

namespace Strata.Core.Content.Specs;

/// <summary>Families of open-ended specs found in content.</summary>
public enum SpecKind
{
    ItemBehavior,
    Brain,
    Condition,
    Effect,
    ProjectileHit,
    TownService,
}

/// <summary>
/// Maps (kind, type name) to a strongly typed parameter class so content can be validated strictly at
/// load time. Behaviour implementations are looked up separately by the systems that execute them.
/// </summary>
public sealed class SpecCatalog
{
    private readonly Dictionary<(SpecKind, string), Type> _types = new();

    public void Register<TParams>(SpecKind kind, string type) where TParams : SpecParams =>
        _types[(kind, type)] = typeof(TParams);

    public bool IsKnown(SpecKind kind, string type) => _types.ContainsKey((kind, type));

    public IEnumerable<string> TypesOf(SpecKind kind) =>
        _types.Keys.Where(k => k.Item1 == kind).Select(k => k.Item2).OrderBy(t => t, StringComparer.Ordinal);

    /// <summary>Parses the spec with its registered parameter type; returns an error message or null.</summary>
    public string? Validate(SpecKind kind, TypedSpec spec)
    {
        if (!_types.TryGetValue((kind, spec.Type), out var paramType))
            return $"Unknown {kind} type '{spec.Type}'. Known: {string.Join(", ", TypesOf(kind))}.";
        try
        {
            var parsed = spec.Raw.Deserialize(paramType, ContentJson.Options);
            if (parsed is IValidatableSpec v) return v.Validate();
            return null;
        }
        catch (JsonException ex)
        {
            return $"Invalid {kind} '{spec.Type}': {ex.Message}";
        }
    }

    /// <summary>Catalog containing every spec type implemented by Strata.Core.</summary>
    public static SpecCatalog CreateDefault()
    {
        var c = new SpecCatalog();
        ItemBehaviorSpecs.Register(c);
        BrainSpecs.Register(c);
        EffectSpecs.Register(c);
        ProjectileHitSpecs.Register(c);
        TownServiceSpecs.Register(c);
        Conditions.ConditionCompiler.RegisterSpecs(c);
        return c;
    }
}

/// <summary>Optional semantic validation for spec parameters beyond JSON shape.</summary>
public interface IValidatableSpec
{
    /// <summary>Returns an error message, or null when valid.</summary>
    string? Validate();
}
