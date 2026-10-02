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
