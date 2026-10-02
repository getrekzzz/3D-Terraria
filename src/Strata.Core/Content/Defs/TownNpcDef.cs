namespace Strata.Core.Content.Defs;

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

    /// <summary>
    /// Lives at a planned landmark (e.g. "dungeon_gate") instead of a house: placed there once its area is loaded,
    /// never housed, and keeps close to it.
    /// </summary>
    public string? Landmark { get; init; }

    /// <summary>Once these hold the resident leaves for good (the crypt's keeper after its warden falls).</summary>
    public TypedSpec[] LeavesWhen { get; init; } = Array.Empty<TypedSpec>();

    /// <summary>Derived kind: landmark keepers never count toward town population or take a house.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public TownNpcKind Kind => Landmark != null ? TownNpcKind.Landmark : TownNpcKind.Resident;

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
