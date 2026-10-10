using System.Numerics;
using Strata.Core.Content.Defs;
using Strata.Core.Session;

namespace Strata.Core.Gameplay.Town;

/// <summary>One reason a resident is happier or unhappier: a reason key, its subject (biome or neighbour) and multiplier.</summary>
public sealed record HappinessReason(string Key, string Subject, float Multiplier);

/// <summary>A resident's price factor (below 1 = happy, cheaper) and why.</summary>
public sealed record HappinessReport(float Factor, List<HappinessReason> Reasons);

/// <summary>
/// Terraria 1.4 happiness: multiplicative factors for loved, liked, disliked and hated biomes (at the home, including
/// the underground layer) and neighbours (within 25 tiles horizontally), the Princess (loved by all), crowding (more
/// than three near neighbours), solitude (few near and far) and being homeless, clamped to the price range.
/// </summary>
public sealed class TownHappiness
{
    private readonly GameWorld _world;
    private readonly TownSystem _town;

    public TownHappiness(GameWorld world, TownSystem town)
    {
        _world = world;
        _town = town;
    }

    private HappinessConfig Config => _town.Config.Happiness;

    public HappinessReport Evaluate(TownNpc npc)
    {
        var reasons = new List<HappinessReason>();
        if (npc.Def.Kind != TownNpcKind.Resident) return new HappinessReport(1f, reasons);
        var at = npc.Home is { } h ? TownSystem.NodeFeet(h) : npc.Position;
        var ctx = _world.Session.ContextAt(npc.Home is { } home ? home + Common.Int3.Up : Physics.VoxelMotion.CellOf(npc.Center));
        bool underground = ctx.Layer is World.WorldLayer.Underground or World.WorldLayer.Cavern;
        int near = 0, far = 0;
        bool princess = false;
        foreach (var o in _town.Residents)
        {
            if (o == npc || !o.CountsAsResident) continue;
            var p = o.Home is { } oh ? TownSystem.NodeFeet(oh) : o.Position;
            if (MathF.Abs(p.Y - at.Y) > Config.VerticalCap) continue;
            float d = new Vector2(p.X - at.X, p.Z - at.Z).Length();
            if (d > Config.FarRadius) continue;
            far++;
            if (d > Config.NearRadius) continue;
            near++;
            bool listed = false;
            foreach (var m in npc.Def.Mood)
                if (m.Neighbor == o.Def.Id)
                {
                    reasons.Add(new HappinessReason(Grade(m.Score) + "Neighbor", o.Def.DisplayName, Multiplier(m.Score)));
                    listed = true;
                }
            if (!listed && o.Def.LovedByAll && !princess)
            {
                princess = true;
                reasons.Add(new HappinessReason("princess", o.Def.DisplayName, Config.Love));
            }
        }
        foreach (var m in npc.Def.Mood)
        {
            if (m.Biome != null && ctx.HasBiome(m.Biome)) reasons.Add(new HappinessReason(Grade(m.Score) + "Biome", BiomeName(m.Biome), Multiplier(m.Score)));
            if (m.Layer == "underground" && underground) reasons.Add(new HappinessReason(Grade(m.Score) + "Biome", "Underground", Multiplier(m.Score)));
        }
        if (near > Config.CrowdLimit) reasons.Add(new HappinessReason("crowded", "", 1f + Config.CrowdStep * (near - Config.CrowdLimit)));
        else if (near <= Config.SolitudeNear && far <= Config.SolitudeFar) reasons.Add(new HappinessReason("solitude", "", Config.Solitude));
        if (npc.Home == null) reasons.Add(new HappinessReason("homeless", "", Config.Homeless));
        float factor = 1f;
        foreach (var r in reasons) factor *= r.Multiplier;
        var town = _town.Config;
        return new HappinessReport(Math.Clamp(factor, town.MinPriceFactor, town.MaxPriceFactor), reasons);
    }

    /// <summary>What the resident says about their happiness (one line per reason, or that they are content).</summary>
    public string Describe(TownNpc npc, HappinessReport report)
    {
        if (report.Reasons.Count == 0) return Line(npc, "content", "");
        return string.Join(" ", report.Reasons.Select(r => Line(npc, r.Key, r.Subject)));
    }

    private string Line(TownNpc npc, string key, string subject)
    {
        string text = npc.Def.HappinessLines.TryGetValue(key, out var own) ? own
            : Config.Lines.TryGetValue(key, out var shared) ? shared : key;
        return text.Replace("{biome}", subject).Replace("{neighbor}", subject);
    }

    private static string Grade(int score) => score switch { >= 2 => "love", 1 => "like", -1 => "dislike", _ => "hate" };

    private float Multiplier(int score) => score switch { >= 2 => Config.Love, 1 => Config.Like, -1 => Config.Dislike, _ => Config.Hate };

    private string BiomeName(string id) => _world.Session.Content.Biomes.TryGet(id, out var b) ? b.DisplayName : id;
}
