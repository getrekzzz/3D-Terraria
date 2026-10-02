namespace Strata.Core.Content.Defs;

/// <summary>How a town NPC lives in the world: housed resident or keeper of a landmark (later: visitor, pet).</summary>
public enum TownNpcKind
{
    Resident,
    Landmark,
}

/// <summary>Something a resident sells. Price defaults to the item's value.</summary>
public sealed class ShopEntry
{
    public string Item { get; init; } = "";

    /// <summary>Price in copper before happiness scaling (null = item value).</summary>
    public long? Price { get; init; }

    /// <summary>Stock appears only while these hold (time, flags, biome of the home…).</summary>
    public TypedSpec[] Conditions { get; init; } = Array.Empty<TypedSpec>();
}

/// <summary>A chat line; the first matching conditions are eligible and one is picked.</summary>
public sealed class DialogueLine
{
    /// <summary>Text with placeholders: {player}, {name} (the resident), {resident:&lt;id&gt;} (another resident's name).</summary>
    public string Text { get; init; } = "";

    public TypedSpec[] Conditions { get; init; } = Array.Empty<TypedSpec>();
}

/// <summary>A happiness preference: a biome around the home or a neighbour. Positive scores are liked.</summary>
public sealed class MoodEntry
{
    public string? Biome { get; init; }
    public string? Neighbor { get; init; }

    /// <summary>+2 loves, +1 likes, −1 dislikes, −2 hates.</summary>
    public int Score { get; init; }
}
