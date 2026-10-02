namespace Strata.Core.Content.Defs;

/// <summary>A timed status effect (buff or debuff) composed of effect specs.</summary>
public sealed class BuffDef : ContentDef
{
    public bool Debuff { get; init; }

    /// <summary>Icon image name under assets/icons/buffs (defaults to the id).</summary>
    public string? Icon { get; init; }
    public string Color { get; init; } = "#ffffff";
    public TypedSpec[] Effects { get; init; } = Array.Empty<TypedSpec>();

    /// <summary>Default duration in ticks when applied by a rule rather than an explicit source.</summary>
    public int Duration { get; init; } = 600;

    /// <summary>Removed on death / cannot be cancelled by the player.</summary>
    public bool Persistent { get; init; }

    /// <summary>
    /// The buff bar shows no countdown (aura and liquid buffs such as Cozy Fire, Heart Lamp and Honey, which are
    /// refreshed every few ticks while the source is near and would otherwise flicker).
    /// </summary>
    public bool HideTimer { get; init; }
}
