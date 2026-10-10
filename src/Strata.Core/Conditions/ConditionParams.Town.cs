using Strata.Core.Content;
using Strata.Core.Content.Defs;
using Strata.Core.Content.Specs;

namespace Strata.Core.Conditions;

/// <summary>Players carry at least <see cref="Min"/> copper worth of coins.</summary>
public sealed class CoinsConditionParams : SpecParams
{
    public long Min { get; init; }
}

/// <summary>A resident town NPC is (or is not) living in the world.</summary>
public sealed class ResidentConditionParams : SpecParams
{
    public string Id { get; init; } = "";
    public bool Present { get; init; } = true;
}

/// <summary>
/// Players carry at least <see cref="Count"/> of an item, of any item with <see cref="Tag"/>, or of any item in an
/// <see cref="ItemCategory"/> (<see cref="Category"/>, e.g. "dye").
/// </summary>
public sealed class HasItemConditionParams : SpecParams, IValidatableSpec
{
    public string? Item { get; init; }
    public string? Tag { get; init; }
    public string? Category { get; init; }
    public int Count { get; init; } = 1;

    public string? Validate()
    {
        int set = (Item != null ? 1 : 0) + (Tag != null ? 1 : 0) + (Category != null ? 1 : 0);
        if (set != 1) return "hasItem needs exactly one of 'item', 'tag' or 'category'";
        return Category != null && !Enum.TryParse<ItemCategory>(Category, ignoreCase: true, out _)
            ? $"hasItem category '{Category}' is not an item category ({string.Join(", ", Enum.GetNames<ItemCategory>())})"
            : null;
    }
}

// --- P5-03 ---

/// <summary>A town NPC was freed from where they were found bound (world flag "rescued_&lt;id&gt;").</summary>
public sealed class RescuedConditionParams : SpecParams
{
    public string Id { get; init; } = "";
}

/// <summary>Players have killed at least <see cref="Min"/> of an enemy (journal).</summary>
public sealed class KilledConditionParams : SpecParams
{
    public string Enemy { get; init; } = "";
    public int Min { get; init; } = 1;
}

/// <summary>Players have picked up an item at least once (journal).</summary>
public sealed class ObtainedConditionParams : SpecParams
{
    public string Item { get; init; } = "";
}

/// <summary>Players have earned an achievement (journal).</summary>
public sealed class AchievedConditionParams : SpecParams
{
    public string Id { get; init; } = "";
}
