namespace Strata.Core.Content.Defs;

public enum EventPhase
{
    /// <summary>Rolled at dusk, ends at dawn.</summary>
    Night,

    /// <summary>Rolled at dawn, ends at dusk.</summary>
    Day,

    /// <summary>Only started explicitly (items, commands, progression triggers).</summary>
    Manual,
}

/// <summary>
/// A world event (blood moon, slime rain, invasions). While active its id is in the condition context's
/// <c>Events</c>, so spawn rules, shops and loot can react; spawning is scaled by the modifiers here.
/// </summary>
public sealed class EventDef : ContentDef
{
    public EventPhase Phase { get; init; } = EventPhase.Night;

    /// <summary>Chance per eligible dusk/dawn that the event starts.</summary>
    public float Chance { get; init; }

    /// <summary>Conditions (world flags, moon phase…) that must hold for a natural start.</summary>
    public TypedSpec[] Conditions { get; init; } = Array.Empty<TypedSpec>();

    /// <summary>Length in ticks; 0 = until the phase ends (dawn/dusk). Manual events need a duration or a kill goal.</summary>
    public int Duration { get; init; }

    /// <summary>Ends after this many kills of enemies carrying <see cref="KillTag"/> (0 = no goal).</summary>
    public int KillGoal { get; init; }

    public string? KillTag { get; init; }

    /// <summary>Enemy spawned (near a player) when the kill goal is reached.</summary>
    public string? FinaleEnemy { get; init; }

    public float SpawnRateMultiplier { get; init; } = 1f;
    public int MaxNearbyBonus { get; init; }

    /// <summary>Sky/fog tint while active (#RRGGBB).</summary>
    public string? SkyTint { get; init; }

    public string? StartMessage { get; init; }
    public string? EndMessage { get; init; }

    /// <summary>Progression flag set when the event completes its goal.</summary>
    public string? CompletionFlag { get; init; }

    /// <summary>Beds cannot be used to skip the night while active.</summary>
    public bool PreventsSleep { get; init; }

    /// <summary>An invasion: it outlasts the day/night change and only ends when its kill goal is met.</summary>
    public bool UntilDefeated { get; init; }

    /// <summary>A manual event that begins the moment this progression flag is raised (the Lunatic Cultist's defeat).</summary>
    public string? StartsWhen { get; init; }

    /// <summary>
    /// Creatures placed across the world's surface when the event starts, spread evenly west to east (the Celestial
    /// Pillars). One whose boss flag is already set is skipped; missing ones return when a saved event is loaded.
    /// </summary>
    public string[] Places { get; init; } = Array.Empty<string>();
}
