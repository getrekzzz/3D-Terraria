namespace Strata.Core.Content.Defs;

/// <summary>A timed status effect (buff or debuff) composed of effect specs.</summary>
public sealed class BuffDef : ContentDef
{
    public bool Debuff { get; init; }
    public string? Icon { get; init; }
    public string Color { get; init; } = "#ffffff";
    public TypedSpec[] Effects { get; init; } = Array.Empty<TypedSpec>();

    /// <summary>Removed on death / cannot be cancelled by the player.</summary>
    public bool Persistent { get; init; }
}
