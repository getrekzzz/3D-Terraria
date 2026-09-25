using Strata.Core.Content.Specs;

namespace Strata.Core.Content.Validation;

public sealed class ContentValidationOptions
{
    public SpecCatalog Specs { get; init; } = SpecCatalog.CreateDefault();

    /// <summary>When set, model/icon paths referenced by content must satisfy this predicate.</summary>
    public Func<string, bool>? AssetExists { get; init; }

    /// <summary>When set, block texture layer names must be in this set (from the texture atlas manifest).</summary>
    public IReadOnlySet<string>? TextureLayers { get; init; }

    /// <summary>Warn about items that have no recipe, loot source, block drop or world source.</summary>
    public bool CheckObtainability { get; init; } = true;
}
