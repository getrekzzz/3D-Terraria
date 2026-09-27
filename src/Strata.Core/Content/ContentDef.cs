using System.Text.Json.Serialization;

namespace Strata.Core.Content;

/// <summary>Base for every data-driven definition.</summary>
public abstract class ContentDef
{
    /// <summary>Stable snake_case identifier, unique per category. Persisted in saves.</summary>
    [JsonRequired]
    public string Id { get; init; } = "";

    /// <summary>Original in-game display name (localization key fallback).</summary>
    public string? Name { get; init; }

    public string? Description { get; init; }

    /// <summary>Reference concepts this entry covers, e.g. "terraria:copper_pickaxe".</summary>
    public string[] Covers { get; init; } = Array.Empty<string>();

    public string[] Tags { get; init; } = Array.Empty<string>();

    /// <summary>Dense per-session numeric id. Never persisted.</summary>
    [JsonIgnore]
    public int RuntimeId { get; internal set; } = -1;

    [JsonIgnore]
    public string SourceFile { get; internal set; } = "";

    [JsonIgnore]
    public string DisplayName => Name ?? Id;

    public bool HasTag(string tag) => Array.IndexOf(Tags, tag) >= 0;

    public override string ToString() => $"{GetType().Name}({Id})";
}
