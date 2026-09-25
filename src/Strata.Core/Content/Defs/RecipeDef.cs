namespace Strata.Core.Content.Defs;

public sealed class RecipeIngredient
{
    /// <summary>Item id. Exactly one of <see cref="Item"/> or <see cref="Group"/> must be set.</summary>
    public string? Item { get; init; }

    /// <summary>Recipe group id (any member item is accepted).</summary>
    public string? Group { get; init; }

    public int Count { get; init; } = 1;
}

/// <summary>A crafting recipe. Stations are station identities provided by tile objects.</summary>
public sealed class RecipeDef : ContentDef
{
    public string Output { get; init; } = "";
    public int Count { get; init; } = 1;
    public RecipeIngredient[] Inputs { get; init; } = Array.Empty<RecipeIngredient>();

    /// <summary>All listed station identities must be within crafting range.</summary>
    public string[] Stations { get; init; } = Array.Empty<string>();

    /// <summary>Environment requirements (near water, biome, progression flags…).</summary>
    public TypedSpec[] Conditions { get; init; } = Array.Empty<TypedSpec>();
}

/// <summary>Interchangeable items for recipes, e.g. any wood.</summary>
public sealed class RecipeGroupDef : ContentDef
{
    public string[] Items { get; init; } = Array.Empty<string>();
}
