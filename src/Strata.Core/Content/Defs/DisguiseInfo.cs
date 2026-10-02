namespace Strata.Core.Content.Defs;

/// <summary>
/// A creature that spawns posing as something harmless (Terraria's Mimics sit as chests, the Nymph as a Lost Girl):
/// while disguised it wears <see cref="Model"/>, stands still, deals no contact damage and fires nothing. It wakes for
/// good when a player hits it, interacts with it (tries to open the "chest"), or — with <see cref="WakeRange"/> — comes
/// that close.
/// </summary>
public sealed class DisguiseInfo
{
    /// <summary>Creature model worn while disguised (assets/models/creatures/&lt;model&gt;.glb).</summary>
    public string Model { get; init; } = "";

    /// <summary>What the disguise passes for (shown where the creature is named while disguised).</summary>
    public string? Name { get; init; }

    /// <summary>A player whose centre comes within this distance (m) of the creature's wakes it; 0 = only hits and interaction do.</summary>
    public float WakeRange { get; init; }

    /// <summary>The interact key on it (opening the chest) wakes it.</summary>
    public bool WakeOnInteract { get; init; } = true;

    /// <summary>Sound cued when it wakes.</summary>
    public string? WakeSound { get; init; }

    /// <summary>Speed (m/s) of the lunge at the player who woke it (a Mimic springs at you); 0 = none.</summary>
    public float Lunge { get; init; }
}
