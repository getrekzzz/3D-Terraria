namespace Strata.Core.Content.Defs;

/// <summary>What forms where two liquids meet.</summary>
public sealed class LiquidMix
{
    /// <summary>The other liquid's id.</summary>
    public string With { get; init; } = "";

    /// <summary>Block created in the cell where they meet.</summary>
    public string Block { get; init; } = "";
}

/// <summary>
/// A liquid (water, lava, honey). The voxel liquid byte stores <see cref="Code"/> (1–7) and a fill level 0–31;
/// everything else about the liquid — how fast and how far it flows, what touching it does, what it forms
/// when it meets another liquid — is data here.
/// </summary>
public sealed class LiquidDef : ContentDef
{
    public const int MaxCode = 7;

    /// <summary>Encoding slot 1–7 stored in voxel data. Persisted in saves: never renumber a shipped liquid.</summary>
    public int Code { get; init; }

    /// <summary>Ticks between flow steps (higher = more viscous).</summary>
    public int FlowInterval { get; init; } = 5;

    /// <summary>Cells a falling column may drop per flow step.</summary>
    public int FallCells { get; init; } = 3;

    /// <summary>A cell shares sideways only with neighbours at least this many levels lower.</summary>
    public int SpreadThreshold { get; init; } = 2;

    /// <summary>Sideways spreading stops once the shared level would fall below this (puddle thickness).</summary>
    public int MinLevel { get; init; } = 3;

    /// <summary>Chance per flow step that a stranded remnant thinner than <see cref="MinLevel"/> evaporates.</summary>
    public float EvaporateChance { get; init; } = 0.15f;

    /// <summary>Block light [r, g, b] 0–15 emitted by this liquid.</summary>
    public int[]? Light { get; init; }

    /// <summary>Surface colour for maps and particles (#RRGGBB).</summary>
    public string Color { get; init; } = "#3070c0";

    /// <summary>Movement speed multiplier while submerged.</summary>
    public float MoveSpeed { get; init; } = 0.6f;

    /// <summary>Upward swim acceleration multiplier (0 = cannot swim up).</summary>
    public float SwimStrength { get; init; } = 1f;

    /// <summary>Velocity kept per tick by bodies inside the liquid (drag).</summary>
    public float Drag { get; init; } = 0.92f;

    /// <summary>
    /// Damage dealt on contact (defense applies), repeated while immunity allows. Creatures tagged
    /// <c>&lt;id&gt;_immune</c> (e.g. <c>lava_immune</c>) and players with a <c>liquidImmune</c> effect are spared.
    /// </summary>
    public int ContactDamage { get; init; }

    /// <summary>Buff applied while in contact (refreshed).</summary>
    public string? ContactBuff { get; init; }

    public int ContactBuffTicks { get; init; } = 60;

    /// <summary>Buffs with any of these tags are removed on contact (water puts out fire).</summary>
    public string[] ClearsBuffTags { get; init; } = Array.Empty<string>();

    /// <summary>Dropped items touching the liquid are destroyed unless tagged <c>liquid_proof</c>.</summary>
    public bool DestroysItems { get; init; }

    /// <summary>Dropped items touching the liquid transmute or come apart into their ingredients (Shimmer).</summary>
    public bool TransmutesItems { get; init; }

    /// <summary>Holding your head under it uses up breath; with none left you drown.</summary>
    public bool Drowns { get; init; } = true;

    /// <summary>Filled bucket item that holds this liquid (collected with an empty bucket).</summary>
    public string? Bucket { get; init; }

    public LiquidMix[] Mixes { get; init; } = Array.Empty<LiquidMix>();
}
