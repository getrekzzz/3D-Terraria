namespace Strata.Core.Content.Defs;

/// <summary>
/// Data-driven enrage: while <see cref="When"/> holds where the boss's target stands (checked twice a second), the boss
/// is <see cref="Gameplay.Entities.Npc.Frenzied"/> and fights with these multipliers or fixed values (Terraria's
/// lethal daylight or out-of-biome rages).
/// </summary>
public sealed class Enrage
{
    public TypedSpec[] When { get; init; } = Array.Empty<TypedSpec>();
    public float Damage { get; init; } = 1f;
    public float Speed { get; init; } = 1f;

    /// <summary>Contact and barrier damage while frenzied, replacing the scaled base damage.</summary>
    public int? SetDamage { get; init; }

    public int? SetDefense { get; init; }

    /// <summary>Once frenzied it stays so until it dies or leaves.</summary>
    public bool Latch { get; init; }

    /// <summary>Announced once each time the rage begins.</summary>
    public string? Message { get; init; }
}

/// <summary>Boss metadata: presentation, arena, progression and body segments (worms).</summary>
public sealed class BossInfo
{
    /// <summary>
    /// A creature that only borrows the boss body (worm segments, free parts) — Wyverns, Devourers, Bone Serpents: no
    /// boss bar, announcements, music or flag, and it despawns like any creature.
    /// </summary>
    public bool Minor { get; init; }

    /// <summary>Shown on the boss bar and in announcements.</summary>
    public string Title { get; init; } = "";

    /// <summary>Progression flag set when defeated.</summary>
    public string? Flag { get; init; }

    /// <summary>More flags raised with <see cref="Flag"/> (either evil boss sets "defeated_evil_boss").</summary>
    public string[] AlsoFlags { get; init; } = Array.Empty<string>();

    /// <summary>Beyond this distance (m) from every player the boss leaves (despawns).</summary>
    public float ArenaRadius { get; init; } = 120f;

    /// <summary>Leaves when day breaks (night bosses).</summary>
    public bool DespawnAtDay { get; init; }

    /// <summary>Music state requested while alive.</summary>
    public string? Music { get; init; }

    /// <summary>Body segments following the head (worms); 0 = single body.</summary>
    public int Segments { get; init; }

    /// <summary>Distance between segment centres (m).</summary>
    public float SegmentSpacing { get; init; } = 1f;

    /// <summary>Collision radius of body segments (m).</summary>
    public float SegmentRadius { get; init; } = 0.6f;

    /// <summary>Creature model for body segments and for the last segment (defaults to the segment model).</summary>
    public string? SegmentModel { get; init; }

    public string? TailModel { get; init; }

    /// <summary>Model per segment index (free parts such as hands); overrides SegmentModel/TailModel.</summary>
    public string[] SegmentModels { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Model stretched from the head to each free part (<see cref="Parts"/>, or <see cref="SegmentModels"/>) as a two-bone arm with an outward
    /// elbow (Skeletron's bones, Prime's struts); modelled along +Y, one unit long.
    /// </summary>
    public string? ArmModel { get; init; }

    /// <summary>Model tiled over the creature's <see cref="Gameplay.Entities.Npc.Barrier"/> (a wall's flesh), and its tile size (m).</summary>
    public string? BarrierModel { get; init; }

    public float BarrierTile { get; init; } = 4f;

    /// <summary>
    /// Bosses summoned alongside this one (a pair of eyes). They share its <see cref="Flag"/>, which is raised only when
    /// the last of the group falls; announcements and the boss bar treat the group as one.
    /// </summary>
    public string[] Companions { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Parts that fight on their own but are fixed to the body (Moon Lord's hands and head): spawned with it, held at
    /// their offsets as it turns, and gone when it goes. Give them this boss's flag so the group falls as one.
    /// </summary>
    public BossPart[] Parts { get; init; } = Array.Empty<BossPart>();

    /// <summary>Condition-driven rage (see <see cref="Defs.Enrage"/>).</summary>
    public Enrage? Enrage { get; init; }

    /// <summary>Boss head icon id (map and boss bar); defaults to the enemy id.</summary>
    public string? Icon { get; init; }

    /// <summary>Roll the loot table only when the whole group falls (a pair's last eye), not for every member.</summary>
    public bool LootOnDefeat { get; init; }

    /// <summary>A gathering at a landmark that calls this boss up when disturbed (the Lunatic Cultist's devotees).</summary>
    public BossRitual? Ritual { get; init; }
}

public sealed class BossPart
{
    public string Enemy { get; init; } = "";

    /// <summary>Offset from the body's centre in metres [right, up, forward], turned with the body's facing.</summary>
    public float[] Offset { get; init; } = { 0f, 0f, 0f };

    /// <summary>
    /// The part falls with the body: it does not keep the group's defeat flag from being raised, and it is removed
    /// (without loot) once the body dies. When false it fights on alone after the body falls.
    /// </summary>
    public bool DiesWithBody { get; init; } = true;

    /// <summary>The part's life counts on the boss bar (Moon Lord's hands and head; not Skeletron's hands).</summary>
    public bool InBossBar { get; init; }

    /// <summary>A left-hand part: presentation mirrors its model (<see cref="Gameplay.Entities.Npc.Mirrored"/>).</summary>
    public bool Mirror { get; init; }
}

/// <summary>
/// Worshippers gather around an altar at a landmark once <see cref="Requires"/> is raised (and until the boss's own
/// flag is), with guards around them. Striking down a worshipper calls the boss up in their place.
/// </summary>
public sealed class BossRitual
{
    /// <summary>World landmark id (e.g. "dungeon_gate").</summary>
    public string Landmark { get; init; } = "";

    public string Requires { get; init; } = "";

    /// <summary>The creature they gather around (the Mysterious Tablet); optional.</summary>
    public string? Altar { get; init; }

    public string Worshipper { get; init; } = "";
    public int Count { get; init; } = 4;
    public string? Guard { get; init; }
    public int Guards { get; init; } = 2;

    /// <summary>Ring radius around the altar (m).</summary>
    public float Radius { get; init; } = 3f;

    /// <summary>They gather only while a player is this close to the landmark (m).</summary>
    public float Range { get; init; } = 60f;
}

/// <summary>A barrier that holds until enough creatures carrying <see cref="KillTag"/> die (the Celestial Pillars' shields).</summary>
public sealed class KillShield
{
    public string KillTag { get; init; } = "";
    public int Kills { get; init; } = 100;
}
