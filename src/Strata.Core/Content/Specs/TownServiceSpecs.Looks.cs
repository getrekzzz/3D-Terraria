namespace Strata.Core.Content.Specs;

/// <summary>
/// A new hairstyle and hair colour for coins (the Stylist): <see cref="Price"/> when the style changes plus
/// <see cref="ColorPrice"/> when the colour does, times the happiness price factor.
/// </summary>
public sealed class HaircutParams : SpecParams, IValidatableSpec
{
    /// <summary>Copper for a new style.</summary>
    public long Price { get; init; } = 20000;

    /// <summary>Copper for a new colour.</summary>
    public long ColorPrice { get; init; } = 10000;

    public string? Validate() => Price < 0 || ColorPrice < 0 ? "price and colorPrice must be >= 0" : null;
}

/// <summary>Swaps a carried item with <see cref="AcceptsTag"/> (a Strange Plant) for a roll of a loot table (the Dye Trader).</summary>
public sealed class DyeTradeParams : SpecParams, IValidatableSpec
{
    public string AcceptsTag { get; init; } = "strange_plant";

    /// <summary>Loot table rolled for each trade.</summary>
    public string Table { get; init; } = "dye_trader_trade";

    /// <summary>Button text in conversation (shown only while a plant is carried).</summary>
    public string Label { get; init; } = "Trade";

    public string? Validate() => string.IsNullOrWhiteSpace(AcceptsTag) || string.IsNullOrWhiteSpace(Table) ? "acceptsTag and table are required" : null;
}
