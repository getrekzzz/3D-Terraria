using System.Globalization;
using System.Numerics;
using System.Text.Json;
using Strata.Core.Content;
using Strata.Core.Content.Loading;

namespace Strata.Core.Presentation;

/// <summary>
/// Grass blades on grass blocks (data/config/grass.json): per block kind colour, height, density, glow and
/// flowers, plus the base clump density. Read by the mesh jobs (scatter) and the renderer (look).
/// </summary>
public sealed class GrassConfig
{
    public const string Path = "data/config/grass.json";

    /// <summary>Clumps per square metre of flat grass at density 1.</summary>
    public float ClumpsPerSquareMeter { get; init; } = 12f;

    /// <summary>Faces tilted further than this (normal.y below it) grow no grass.</summary>
    public float MinUpward { get; init; } = 0.55f;

    /// <summary>Kinds keyed by block id.</summary>
    public Dictionary<string, GrassKind> Kinds { get; init; } = new();

    public static GrassConfig Load(IDataSource data) =>
        JsonSerializer.Deserialize<GrassConfig>(data.ReadText(Path), ContentJson.Options) ?? throw new JsonException($"{Path} is empty");

    /// <summary>Kinds in a stable order (by block id); <see cref="Meshing.GrassClump.Kind"/> indexes this list.</summary>
    public IReadOnlyList<KeyValuePair<string, GrassKind>> Ordered =>
        _ordered ??= Kinds.OrderBy(k => k.Key, StringComparer.Ordinal).ToList();

    private List<KeyValuePair<string, GrassKind>>? _ordered;

    /// <summary>Problems with the file against the content (unknown blocks, grass blocks without a kind, bad ranges).</summary>
    public List<string> Validate(ContentDatabase content)
    {
        var problems = new List<string>();
        if (ClumpsPerSquareMeter is < 1f or > 64f) problems.Add("clumpsPerSquareMeter must be within 1–64");
        if (MinUpward is < 0f or > 1f) problems.Add("minUpward must be within 0–1");
        if (Kinds.Count > 255) problems.Add("at most 255 kinds");
        foreach (var (id, kind) in Kinds)
        {
            if (!content.Blocks.TryGet(id, out var block)) problems.Add($"kind '{id}' is not a block");
            else if (!block.HasTag("grass")) problems.Add($"kind '{id}' names a block without the 'grass' tag");
            foreach (var problem in kind.Validate()) problems.Add($"{id}: {problem}");
        }
        foreach (var block in content.Blocks.All)
            if (block.HasTag("grass") && !Kinds.ContainsKey(block.Id))
                problems.Add($"block '{block.Id}' is tagged 'grass' but has no grass kind");
        return problems;
    }
}

/// <summary>The look of one block's grass.</summary>
public sealed class GrassKind
{
    public string Color { get; init; } = "#5b8a2e";

    /// <summary>Clump height range in metres [min, max].</summary>
    public float[] Height { get; init; } = { 0.2f, 0.4f };

    /// <summary>Relative clump density (0–4).</summary>
    public float Density { get; init; } = 1f;

    /// <summary>Self-illumination of the blade tips (0–1).</summary>
    public float Glow { get; init; }

    /// <summary>Chance (0–1) that a clump carries a flower head.</summary>
    public float Flowers { get; init; }

    public string[] FlowerColors { get; init; } = Array.Empty<string>();

    public float MinHeight => Height[0];
    public float MaxHeight => Height[^1];

    public IEnumerable<string> Validate()
    {
        if (!IsHex(Color)) yield return $"color '{Color}' is not #rrggbb";
        if (Height.Length != 2 || Height[0] <= 0f || Height[1] < Height[0] || Height[1] > 3f) yield return "height must be [min, max] with 0 < min ≤ max ≤ 3";
        if (Density is < 0f or > 4f) yield return "density must be within 0–4";
        if (Glow is < 0f or > 1f) yield return "glow must be within 0–1";
        if (Flowers is < 0f or > 1f) yield return "flowers must be within 0–1";
        if (Flowers > 0f && FlowerColors.Length == 0) yield return "flowers need flowerColors";
        if (FlowerColors.Length > 254) yield return "at most 254 flowerColors";
        foreach (var c in FlowerColors)
            if (!IsHex(c)) yield return $"flower colour '{c}' is not #rrggbb";
    }

    /// <summary>sRGB colour in [0, 1] from #rrggbb.</summary>
    public static Vector3 Rgb(string hex)
    {
        if (!IsHex(hex)) throw new FormatException($"'{hex}' is not #rrggbb");
        int v = int.Parse(hex.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return new Vector3((v >> 16) & 255, (v >> 8) & 255, v & 255) / 255f;
    }

    private static bool IsHex(string s) =>
        s.Length == 7 && s[0] == '#' && s.AsSpan(1).IndexOfAnyExcept("0123456789abcdefABCDEF") < 0;
}
