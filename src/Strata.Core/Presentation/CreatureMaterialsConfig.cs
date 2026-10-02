using System.Text.Json;
using Strata.Core.Content;
using Strata.Core.Content.Defs;
using Strata.Core.Content.Loading;

namespace Strata.Core.Presentation;

/// <summary>
/// What creatures are made of (data/config/creature_materials.json): each material's hit and death sounds, hit dust and
/// death gore. <see cref="Resolve"/> picks a creature's material from its own <c>material</c>, else its model, else its
/// tags, else flesh.
/// </summary>
public sealed class CreatureMaterialsConfig
{
    public const string Path = "data/config/creature_materials.json";
    public const string Default = "flesh";

    /// <summary>The effect recipes a material may name (game/Rendering/EffectKit.cs).</summary>
    public static readonly string[] Recipes = { "Chips", "Dust", "Sparks", "Puff", "Splash", "Leaves" };

    public Dictionary<string, CreatureMaterial> Materials { get; init; } = new();
    public Dictionary<string, string> ByModel { get; init; } = new();
    public Dictionary<string, string> ByTag { get; init; } = new();

    public static CreatureMaterialsConfig Load(IDataSource data) =>
        JsonSerializer.Deserialize<CreatureMaterialsConfig>(data.ReadText(Path), ContentJson.Options) ?? throw new JsonException($"{Path} is empty");

    public string ResolveId(EnemyDef def)
    {
        if (def.Material != null) return def.Material;
        if (def.Model != null && ByModel.TryGetValue(def.Model, out var byModel)) return byModel;
        foreach (var (tag, material) in ByTag)
            if (def.HasTag(tag)) return material;
        return Default;
    }

    public CreatureMaterial Resolve(EnemyDef def) =>
        Materials.TryGetValue(ResolveId(def), out var m) ? m : Materials[Default];

    public List<string> Validate(ContentDatabase content)
    {
        var problems = new List<string>();
        if (!Materials.ContainsKey(Default)) problems.Add($"material '{Default}' is required");
        foreach (var (id, m) in Materials)
        {
            if (Array.IndexOf(Recipes, m.Dust.Recipe) < 0) problems.Add($"{id}: unknown dust recipe '{m.Dust.Recipe}'");
            if (m.Gore.Pieces is < 0 or > 12) problems.Add($"{id}: gore pieces must be within 0–12");
            if (m.Gore.Shape is not ("box" or "sphere")) problems.Add($"{id}: gore shape must be box or sphere");
        }
        foreach (var (model, material) in ByModel)
            if (!Materials.ContainsKey(material)) problems.Add($"byModel {model}: unknown material '{material}'");
        foreach (var (tag, material) in ByTag)
            if (!Materials.ContainsKey(material)) problems.Add($"byTag {tag}: unknown material '{material}'");
        foreach (var e in content.Enemies.All)
            if (e.Material != null && !Materials.ContainsKey(e.Material)) problems.Add($"enemy {e.Id}: unknown material '{e.Material}'");
        return problems;
    }
}

public sealed class CreatureMaterial
{
    public string HitSound { get; init; } = "hit";
    public string DeathSound { get; init; } = "npc_death";
    public MaterialDust Dust { get; init; } = new();
    public MaterialGore Gore { get; init; } = new();
}

public sealed class MaterialDust
{
    public string Recipe { get; init; } = "Dust";
    public string Color { get; init; } = "#a01818";

    /// <summary>Use the creature's tint instead of <see cref="Color"/> (slimes, spirits).</summary>
    public bool UseTint { get; init; }
}

public sealed class MaterialGore
{
    public int Pieces { get; init; } = 3;
    public string Color { get; init; } = "#8a2424";
    public bool UseTint { get; init; }
    public string Shape { get; init; } = "box";
}
