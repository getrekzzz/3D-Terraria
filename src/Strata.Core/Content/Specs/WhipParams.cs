namespace Strata.Core.Content.Specs;

/// <summary>A whip's lash (P3-14): reach, hit points, falloff and the debuffs it leaves.</summary>
public sealed class WhipParams : SpecParams, IValidatableSpec
{
    /// <summary>Reach of the lash when fully extended, in cells.</summary>
    public float Length { get; init; } = 10f;

    /// <summary>Points along the lash tested for hits.</summary>
    public int Segments { get; init; } = 20;

    /// <summary>Each creature after the first takes this much less (compounding): ×(1 − falloff)^hits so far.</summary>
    public float Falloff { get; init; } = 0.3f;

    /// <summary>The debuff a lash leaves on what it hits (a <c>minionTag</c> buff), and for how long (ticks).</summary>
    public string? TagBuff { get; init; }

    public int TagTicks { get; init; } = 240;

    /// <summary>Another debuff the lash inflicts (Snapthorn: poisoned), and for how long (ticks).</summary>
    public string? Inflict { get; init; }

    public int InflictTicks { get; init; } = 240;

    /// <summary>The lash's colour on screen (P3-15).</summary>
    public string Color { get; init; } = "#7a5232";

    public string? Validate() =>
        !System.Text.RegularExpressions.Regex.IsMatch(Color, "^#[0-9a-fA-F]{6}$") ? "color must be #RRGGBB"
        : Length is <= 0f or > 40f ? "length must be within (0, 40] cells"
        : Segments is < 4 or > 64 ? "segments must be within 4–64"
        : Falloff is < 0f or >= 1f ? "falloff must be within [0, 1)"
        : TagTicks < 1 || InflictTicks < 1 ? "tagTicks and inflictTicks must be >= 1" : null;
}
