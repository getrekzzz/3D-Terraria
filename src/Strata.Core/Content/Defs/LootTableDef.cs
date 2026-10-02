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
}
