namespace Strata.Core.Content.Defs;

/// <summary>What a tip is about (the book's tips tab groups them).</summary>
public enum GuideTipTopic
{
    Controls,
    Building,
    Combat,
    Town,
    Exploration,
}

/// <summary>
/// A tip for the Guide's book (data/guide/tips.json). <see cref="Text"/> may name input actions as
/// <c>{key:action}</c>, shown as the bound key; a controls tip names its <see cref="Action"/> for the controls list.
/// </summary>
public sealed class GuideTipDef : ContentDef
{
    public string Text { get; init; } = "";
    public GuideTipTopic Topic { get; init; }
    public string? Action { get; init; }
}
