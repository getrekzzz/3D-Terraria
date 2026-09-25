namespace Strata.Core.Content.Defs;

public enum FlagKind
{
    Boss, Event, World, Npc, Discovery,
}

/// <summary>A persistent world progression flag (boss defeated, hardmode, event completed…).</summary>
public sealed class ProgressionFlagDef : ContentDef
{
    public FlagKind Kind { get; init; } = FlagKind.World;

    /// <summary>Flags that must be set before this one can be set (progression DAG).</summary>
    public string[] Requires { get; init; } = Array.Empty<string>();

    /// <summary>Progression stage (reference model) this flag advances the world to.</summary>
    public string? Stage { get; init; }

    /// <summary>Raised automatically as soon as every flag in <see cref="Requires"/> is set.</summary>
    public bool AutoSet { get; init; }

    /// <summary>Shown to players when the flag is raised.</summary>
    public string? Announce { get; init; }
}
