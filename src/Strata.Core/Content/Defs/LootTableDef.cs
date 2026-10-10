namespace Strata.Core.Content.Defs;

public sealed class LootEntry
{
    public string? Item { get; init; }

    /// <summary>Nested table id (alternative to <see cref="Item"/>).</summary>
    public string? Table { get; init; }

    public int Min { get; init; } = 1;
    public int Max { get; init; } = 1;

    /// <summary>Relative weight within a weighted roll.</summary>
    public float Weight { get; init; } = 1f;

    /// <summary>Independent chance [0,1] used when the table mode is "each".</summary>
    public float Chance { get; init; } = 1f;

    public TypedSpec[] Conditions { get; init; } = Array.Empty<TypedSpec>();

    /// <summary>Added to <see cref="Chance"/> (or <see cref="Weight"/> in a weighted roll) per unit of world depth
    /// (<see cref="Conditions.ConditionContext.WorldDepth"/>, 0 at sea level to 1 at the underworld), so rare finds grow
    /// likelier deeper down.</summary>
    public float DepthBonus { get; init; }
}

/// <summary>How a table's coins and stack sizes grow with world depth: each is multiplied by 1 + k × depth (depth clamped to [0, 1]).</summary>
public sealed class LootDepthScale
{
    public float Coins { get; init; }

    /// <summary>For stackable items only (potions, bars, ammunition); single items stay single.</summary>
    public float Counts { get; init; }
}

public enum LootMode
{
    /// <summary>Every entry rolls its own <see cref="LootEntry.Chance"/>.</summary>
    Each,

    /// <summary>Pick <see cref="LootTableDef.Rolls"/> entries by weight.</summary>
    Weighted,
}

/// <summary>A drop table for enemies, chests, pots, bosses and structures.</summary>
public sealed class LootTableDef : ContentDef
{
    /// <summary>Deepest nested table that is still rolled (the top table is depth 0); deeper ones give nothing.</summary>
    public const int MaxNesting = 8;

    public LootMode Mode { get; init; } = LootMode.Each;
    public int Rolls { get; init; } = 1;
    public LootEntry[] Entries { get; init; } = Array.Empty<LootEntry>();

    /// <summary>Coin range in copper [min, max].</summary>
    public int[]? Coins { get; init; }

    /// <summary>Richer coins and stacks the deeper the container lies; nested tables rolled from this one share it.</summary>
    public LootDepthScale? DepthScale { get; init; }
}
