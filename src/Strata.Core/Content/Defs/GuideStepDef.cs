namespace Strata.Core.Content.Defs;

/// <summary>Where in the game a guide step belongs (the book groups its checklist by it).</summary>
public enum GuideStage
{
    PreHardmode,
    Hardmode,
    PostMoonLord,
}

/// <summary>
/// One step of the progression the Guide advises (data/guide/steps.json): done when every <see cref="DoneWhen"/>
/// trigger holds (achievement trigger specs), offered once every <see cref="After"/> step is done and every
/// <see cref="ShowWhen"/> trigger holds, in <see cref="Order"/>.
/// </summary>
public sealed class GuideStepDef : ContentDef
{
    /// <summary>The Guide's advice while this is the next step.</summary>
    public string Text { get; init; } = "";

    public int Order { get; init; }
    public string[] After { get; init; } = Array.Empty<string>();
    public TypedSpec[] DoneWhen { get; init; } = Array.Empty<TypedSpec>();
    public TypedSpec[] ShowWhen { get; init; } = Array.Empty<TypedSpec>();
    public string[] RelatedItems { get; init; } = Array.Empty<string>();
    public string[] RelatedEnemies { get; init; } = Array.Empty<string>();
    public GuideStage Stage { get; init; }
}
