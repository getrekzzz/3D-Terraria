namespace Strata.Core.Content.Defs;

/// <summary>Achievement groups, as the reference game's achievements screen sorts them.</summary>
public enum AchievementCategory
{
    Explorer,
    Collector,
    Slayer,
    Challenger,
}

/// <summary>
/// A data-driven achievement (data/achievements.json): unlocked once per character when its trigger
/// (<see cref="Specs.AchievementSpecs"/>) is met, shown with <see cref="Icon"/> in a popup.
/// </summary>
public sealed class AchievementDef : ContentDef
{
    /// <summary>Item whose icon the popup and the book show.</summary>
    public string? Icon { get; init; }

    public AchievementCategory Category { get; init; } = AchievementCategory.Explorer;

    public TypedSpec Trigger { get; init; } = TypedSpec.Of(Specs.AchievementSpecs.Flag);
}
