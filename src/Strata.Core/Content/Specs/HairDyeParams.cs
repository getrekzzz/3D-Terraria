namespace Strata.Core.Content.Specs;

/// <summary>How a hair dye colours the hair (see <see cref="Gameplay.Player.HairDyes"/>).</summary>
public enum HairDyeMode
{
    /// <summary>A fixed <see cref="HairDyeParams.Color"/>.</summary>
    Static,

    /// <summary>Redder the fuller the user's life.</summary>
    Life,

    /// <summary>Bluer the fuller the user's mana.</summary>
    Mana,

    /// <summary>By the world layer the user stands in, from sky blue to underworld red.</summary>
    Depth,

    /// <summary>Gold at noon, deep blue at midnight.</summary>
    Time,

    /// <summary>The colour of the highest coin carried.</summary>
    Money,

    /// <summary>From white at rest to red at full run.</summary>
    Speed,

    /// <summary>Cycles through the hues.</summary>
    Rainbow,

    /// <summary>Washes the dye out (Hair Dye Remover).</summary>
    Remove,
}

/// <summary>A hair dye item: using it sets the character's hair dye (consumed when the item is consumable).</summary>
public sealed class HairDyeParams : SpecParams, IValidatableSpec
{
    public HairDyeMode Mode { get; init; } = HairDyeMode.Static;

    /// <summary>Hex colour of a static dye.</summary>
    public string Color { get; init; } = "#ffffff";

    public string? Validate() => Mode == HairDyeMode.Static && !Gameplay.Player.HairDyes.TryParseHex(Color, out _) ? $"color '{Color}' is not #rrggbb" : null;
}
