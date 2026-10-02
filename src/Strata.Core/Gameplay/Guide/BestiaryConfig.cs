using System.Text.Json;
using Strata.Core.Content;
using Strata.Core.Content.Loading;

namespace Strata.Core.Gameplay.Guide;

/// <summary>Bestiary unlock tuning (data/config/bestiary.json).</summary>
public sealed class BestiaryConfig
{
    public const string Path = "data/config/bestiary.json";

    /// <summary>Kills that reveal an entry's stats.</summary>
    public int KillsForStats { get; init; } = 1;

    /// <summary>Kills that reveal an entry's drops (ordinary creatures).</summary>
    public int KillsForDrops { get; init; } = 25;

    public int BossKillsForDrops { get; init; } = 1;

    /// <summary>Kills that reveal the drops of a creature tagged "rare".</summary>
    public int RareKillsForDrops { get; init; } = 1;

    /// <summary>Creatures this close to a player (m) count as seen.</summary>
    public float SeenRadius { get; init; } = 20f;

    /// <summary>A kill credits every player this close (m); boss kills credit everyone.</summary>
    public float CreditRadius { get; init; } = 60f;

    public static BestiaryConfig Load(IDataSource data) =>
        JsonSerializer.Deserialize<BestiaryConfig>(data.ReadText(Path), ContentJson.Options) ?? throw new JsonException($"{Path} is empty");

    public List<string> Validate()
    {
        var problems = new List<string>();
        if (KillsForStats < 1) problems.Add("killsForStats must be >= 1");
        if (KillsForDrops < KillsForStats || BossKillsForDrops < 1 || RareKillsForDrops < 1) problems.Add("kills for drops must be >= 1 and not below killsForStats");
        if (SeenRadius <= 0 || CreditRadius <= 0) problems.Add("seenRadius and creditRadius must be > 0");
        return problems;
    }
}
