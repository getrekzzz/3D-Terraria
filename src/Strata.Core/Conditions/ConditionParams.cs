using Strata.Core.Content;
using Strata.Core.Content.Specs;

namespace Strata.Core.Conditions;

public sealed class BiomeConditionParams : SpecParams, IValidatableSpec
{
    public string[] Any { get; init; } = Array.Empty<string>();
    public string[] None { get; init; } = Array.Empty<string>();

    public string? Validate() => Any.Length == 0 && None.Length == 0 ? "biome condition needs 'any' or 'none'" : null;
}

public sealed class LayerConditionParams : SpecParams, IValidatableSpec
{
    public string[] Any { get; init; } = Array.Empty<string>();

    public string? Validate()
    {
        foreach (var l in Any)
            if (!Enum.TryParse<World.WorldLayer>(l, ignoreCase: true, out _)) return $"unknown layer '{l}'";
        return Any.Length == 0 ? "layer condition needs 'any'" : null;
    }
}

public sealed class TimeConditionParams : SpecParams, IValidatableSpec
{
    /// <summary>"day", "night", or null to use the hour window.</summary>
    public string? Is { get; init; }

    public float FromHour { get; init; }
    public float ToHour { get; init; } = 24f;

    public string? Validate() => Is is null or "day" or "night" ? null : $"time.is must be day|night, got '{Is}'";
}

/// <summary>The local weather is one of <see cref="Any"/> (clear, rain, storm, snow, blizzard, sandstorm, or windy).</summary>
public sealed class WeatherConditionParams : SpecParams, IValidatableSpec
{
    public string[] Any { get; init; } = Array.Empty<string>();

    /// <summary>False inverts it: none of the kinds holds.</summary>
    public bool Active { get; init; } = true;

    public string? Validate()
    {
        foreach (var k in Any)
            if (k != "windy" && !Enum.TryParse<Gameplay.Weather.LocalWeather>(k, ignoreCase: true, out _)) return $"unknown weather '{k}'";
        return Any.Length == 0 ? "weather condition needs 'any'" : null;
    }
}

public sealed class FlagConditionParams : SpecParams
{
    public string Id { get; init; } = "";
    public bool Set { get; init; } = true;
}

public sealed class EventConditionParams : SpecParams
{
    public string Id { get; init; } = "";
    public bool Active { get; init; } = true;

    /// <summary>Holds only once the running event has counted at least this many kills (its later waves).</summary>
    public int MinKills { get; init; }
}

/// <summary>The world is (or is not) an Expert-rules world: Expert or Master, never Classic or Journey.</summary>
public sealed class ExpertConditionParams : SpecParams
{
    public bool Is { get; init; } = true;
}

public sealed class RangeConditionParams : SpecParams
{
    public float Min { get; init; } = float.MinValue;
    public float Max { get; init; } = float.MaxValue;
}

public sealed class MoonConditionParams : SpecParams
{
    public int[] Phases { get; init; } = Array.Empty<int>();
}

public sealed class NearLiquidParams : SpecParams
{
    public string Liquid { get; init; } = "water";
}

public sealed class ChanceConditionParams : SpecParams, IValidatableSpec
{
    public float P { get; init; } = 0.5f;

    public string? Validate() => P is < 0 or > 1 ? "p must be in [0,1]" : null;
}

public sealed class CompositeConditionParams : SpecParams
{
    public TypedSpec[] Of { get; init; } = Array.Empty<TypedSpec>();
}

/// <summary>A player wears an item (armor or accessory slot): the Clothier's Red Hat.</summary>
public sealed class WearingConditionParams : SpecParams
{
    public string Item { get; init; } = "";
}

/// <summary>The world's evil: "corruption" or "crimson".</summary>
public sealed class EvilConditionParams : SpecParams
{
    public string Is { get; init; } = "";
}

/// <summary>The catch comes from one of these liquids ("water", "honey", "lava").</summary>
public sealed class FishingLiquidConditionParams : SpecParams
{
    public string[] Any { get; init; } = Array.Empty<string>();
}

/// <summary>A quest fish entry: holds only while the Angler wants <see cref="Fish"/> today.</summary>
public sealed class AnglerQuestConditionParams : SpecParams
{
    public string Fish { get; init; } = "";
}

/// <summary>True in worlds created with this secret seed ("for_the_worthy", "drunk_world", "celebration", …).</summary>
public sealed class SecretSeedConditionParams : SpecParams, Content.Specs.IValidatableSpec
{
    public string Is { get; init; } = "";

    public string? Validate() => World.SecretSeeds.FromId(Is) == World.SecretSeed.None ? $"unknown secret seed '{Is}'" : null;
}

/// <summary>The seasonal event running is <see cref="Is"/> ("halloween", "christmas", or "none").</summary>
public sealed class SeasonConditionParams : SpecParams, Content.Specs.IValidatableSpec
{
    public static readonly string[] Values = { "halloween", "christmas", "none" };

    public string Is { get; init; } = "";

    public string? Validate() => Array.IndexOf(Values, Is) < 0 ? $"season must be one of {string.Join(", ", Values)}" : null;
}
