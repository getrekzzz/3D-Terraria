namespace Strata.Core.Conditions;

/// <summary>A compiled, reusable predicate built from a condition spec.</summary>
public interface ICondition
{
    bool Evaluate(ConditionContext context);
}

/// <summary>Always true (used for empty condition lists).</summary>
public sealed class TrueCondition : ICondition
{
    public static readonly TrueCondition Instance = new();

    public bool Evaluate(ConditionContext context) => true;
}

/// <summary>Conjunction of conditions.</summary>
public sealed class AllCondition : ICondition
{
    private readonly ICondition[] _parts;

    public AllCondition(ICondition[] parts) => _parts = parts;

    public bool Evaluate(ConditionContext context)
    {
        foreach (var p in _parts)
            if (!p.Evaluate(context)) return false;
        return true;
    }
}

/// <summary>Disjunction of conditions.</summary>
public sealed class AnyCondition : ICondition
{
    private readonly ICondition[] _parts;

    public AnyCondition(ICondition[] parts) => _parts = parts;

    public bool Evaluate(ConditionContext context)
    {
        foreach (var p in _parts)
            if (p.Evaluate(context)) return true;
        return false;
    }
}

public sealed class NotCondition : ICondition
{
    private readonly ICondition _inner;

    public NotCondition(ICondition inner) => _inner = inner;

    public bool Evaluate(ConditionContext context) => !_inner.Evaluate(context);
}

/// <summary>Adapter for simple lambda conditions.</summary>
public sealed class DelegateCondition : ICondition
{
    private readonly Func<ConditionContext, bool> _fn;

    public DelegateCondition(Func<ConditionContext, bool> fn) => _fn = fn;

    public bool Evaluate(ConditionContext context) => _fn(context);
}
