namespace Strata.Core.Content.Defs;

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

/// <summary>
/// A town resident (guide, merchant, nurse…): arrives when its conditions hold and a vacant valid home exists, lives
/// in that home, wanders by day and goes home at night, talks, sells, and may offer a service. Prices scale with
/// happiness from the home's biome and neighbours.
/// </summary>
public sealed class TownNpcDef : ContentDef
{
    /// <summary>Rigged model under assets/models/creatures (null = the id).</summary>
    public string? Model { get; init; }

    /// <summary>Personal names picked at arrival.</summary>
    public string[] Names { get; init; } = Array.Empty<string>();

    /// <summary>Conditions for moving in (checked with world, town and player state).</summary>
    public TypedSpec[] Arrival { get; init; } = Array.Empty<TypedSpec>();

    /// <summary>Present from world creation (does not need to meet <see cref="Arrival"/>).</summary>
    public bool Starter { get; init; }

    public int Life { get; init; } = 250;
    public int Defense { get; init; } = 15;

    /// <summary>Walking speed (m/s).</summary>
    public float Speed { get; init; } = 1.6f;

    public ShopEntry[] Shop { get; init; } = Array.Empty<ShopEntry>();

    /// <summary>Extra action offered in conversation (see <see cref="Specs.TownServiceSpecs"/>).</summary>
    public TypedSpec? Service { get; init; }

    public DialogueLine[] Lines { get; init; } = Array.Empty<DialogueLine>();
    public MoodEntry[] Mood { get; init; } = Array.Empty<MoodEntry>();
}
