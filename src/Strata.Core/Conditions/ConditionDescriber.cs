using System.Globalization;
using Strata.Core.Content;
using Strata.Core.Content.Specs;

namespace Strata.Core.Conditions;

/// <summary>
/// Condition specs as players read them ("at night", "after the Eye of Cthulhu is defeated", "in the Jungle", "during a
/// Blood Moon", "the Merchant lives in town"), using content names. Shared by the Guide book (recipes, drops,
/// habitats, shops). Unknown types read as their type name, so new conditions never break the book.
/// </summary>
public static class ConditionDescriber
{
    private static readonly string[] MoonNames =
        { "full moon", "waning gibbous moon", "third quarter moon", "waning crescent moon", "new moon", "waxing crescent moon", "first quarter moon", "waxing gibbous moon" };

    /// <summary>All conditions joined with ", " (empty when there are none).</summary>
    public static string Describe(IReadOnlyList<TypedSpec> specs, ContentDatabase content)
    {
        if (specs.Count == 0) return "";
        var parts = new List<string>(specs.Count);
        foreach (var s in specs)
        {
            var text = Describe(s, content);
            if (text.Length > 0) parts.Add(text);
        }
        return string.Join(", ", parts);
    }

    public static string Describe(TypedSpec spec, ContentDatabase content)
    {
        try { return DescribeOne(spec, content); }
        catch (Exception e) when (e is System.Text.Json.JsonException or InvalidOperationException or ArgumentException)
        {
            return spec.Type;
        }
    }

    private static string DescribeOne(TypedSpec s, ContentDatabase c)
    {
        switch (s.Type)
        {
            case "biome":
            {
                var p = s.Parse<BiomeConditionParams>();
                var any = p.Any.Length > 0 ? "in the " + Or(p.Any.Select(b => Name(c.Biomes, b))) : "";
                var none = p.None.Length > 0 ? "not in the " + Or(p.None.Select(b => Name(c.Biomes, b))) : "";
                return Join(any, none);
            }
            case "layer":
                return "in the " + Or(s.Parse<LayerConditionParams>().Any.Select(Humanize)) + " layer";
            case "time":
            {
                var p = s.Parse<TimeConditionParams>();
                return p.Is switch
                {
                    "day" => "during the day",
                    "night" => "at night",
                    _ => $"between {Hour(p.FromHour)} and {Hour(p.ToHour)}",
                };
            }
            case "flag":
            {
                var p = s.Parse<FlagConditionParams>();
                string name = Name(c.Flags, p.Id);
                if (p.Id == "hardmode") return p.Set ? "in Hardmode" : "before Hardmode";
                if (name.EndsWith(" defeated", StringComparison.Ordinal))
                {
                    string who = name[..^" defeated".Length];
                    return p.Set ? $"after {Article(who)} is defeated" : $"before {Article(who)} is defeated";
                }
                return (p.Set ? "after: " : "before: ") + name;
            }
            case "event":
            {
                var p = s.Parse<EventConditionParams>();
                string name = Name(c.Events, p.Id);
                if (!p.Active) return $"not during {Article(name, "a")}";
                return p.MinKills > 0 ? $"during {Article(name, "a")} (after {p.MinKills} kills)" : $"during {Article(name, "a")}";
            }
            case "depth":
                return Range(s.Parse<RangeConditionParams>(), "depth", "");
            case "worldDepth":
                return WorldDepth(s.Parse<RangeConditionParams>());
            case "light":
                return Range(s.Parse<RangeConditionParams>(), "light", "");
            case "moon":
                return "during a " + Or(s.Parse<MoonConditionParams>().Phases.Select(i => MoonNames[((i % 8) + 8) % 8]));
            case "nearLiquid":
                return "near " + Name(c.Liquids, s.Parse<NearLiquidParams>().Liquid);
            case "chance":
                return $"{s.Parse<ChanceConditionParams>().P * 100f:0.##}% chance";
            case "coins":
                return "carrying " + Coins(s.Parse<CoinsConditionParams>().Min);
            case "maxLife":
                return Range(s.Parse<RangeConditionParams>(), "max life", "");
            case "resident":
            {
                var p = s.Parse<ResidentConditionParams>();
                string who = Article(Name(c.TownNpcs, p.Id));
                return p.Present ? $"{Capital(who)} lives in town" : $"{Capital(who)} does not live in town";
            }
            case "residents":
            {
                var p = s.Parse<RangeConditionParams>();
                return p.Max < float.MaxValue / 2 ? $"{p.Min:0}–{p.Max:0} residents" : $"at least {p.Min:0} residents";
            }
            case "hasItem":
            {
                var p = s.Parse<HasItemConditionParams>();
                string what = p.Item != null ? Name(c.Items, p.Item) : "any " + Humanize(p.Tag!);
                return p.Count > 1 ? $"carrying {p.Count} {what}" : $"carrying {what}";
            }
            case "rescued":
                return $"after rescuing {Article(Name(c.TownNpcs, s.Parse<RescuedConditionParams>().Id))}";
            case "bestiary":
                return $"bestiary {s.Parse<RangeConditionParams>().Min:0}% complete";
            case "anglerQuests":
                return $"after {s.Parse<RangeConditionParams>().Min:0} Angler quests";
            case "killed":
            {
                var p = s.Parse<KilledConditionParams>();
                string who = Name(c.Enemies, p.Enemy);
                return p.Min > 1 ? $"after defeating {p.Min} {who}" : $"after defeating {Article(who, "a")}";
            }
            case "obtained":
                return "once you have found " + Name(c.Items, s.Parse<ObtainedConditionParams>().Item);
            case "achieved":
                return "after the achievement " + Name(c.Achievements, s.Parse<AchievedConditionParams>().Id);
            case "wearing":
                return "wearing " + Name(c.Items, s.Parse<WearingConditionParams>().Item);
            case "difficulty":
            {
                var p = s.Parse<RangeConditionParams>();
                return p.Min >= 2 ? "in Master mode" : p.Min >= 1 ? "in Expert mode or harder" : p.Max < 1 ? "in Classic mode" : "difficulty";
            }
            case "expert":
                return s.Parse<ExpertConditionParams>().Is ? "in Expert mode" : "not in Expert mode";
            case "defense":
                return Range(s.Parse<RangeConditionParams>(), "defense", "");
            case "evil":
                return "in a " + Humanize(s.Parse<EvilConditionParams>().Is) + " world";
            case "secretSeed":
                return "in the " + Humanize(s.Parse<SecretSeedConditionParams>().Is) + " seed";
            case "weather":
            {
                var p = s.Parse<WeatherConditionParams>();
                string kinds = Or(p.Any.Select(Humanize));
                return p.Active ? "while " + kinds : "unless " + kinds;
            }
            case "fishingPower":
                return Range(s.Parse<RangeConditionParams>(), "fishing power", "");
            case "fishingLiquid":
                return "fishing in " + Or(s.Parse<FishingLiquidConditionParams>().Any.Select(l => Name(c.Liquids, l)));
            case "anglerQuest":
                return "for the Angler's quest (" + Name(c.Items, s.Parse<AnglerQuestConditionParams>().Fish) + ")";
            case "season":
            {
                var p = s.Parse<SeasonConditionParams>();
                return p.Is == "none" ? "outside seasonal events" : "during " + Humanize(p.Is);
            }
            case "worldRing":
                return Range(s.Parse<RangeConditionParams>(), "distance from the world centre", "");
            case "all":
                return Describe(s.Parse<CompositeConditionParams>().Of, c);
            case "any":
                return Or(s.Parse<CompositeConditionParams>().Of.Select(x => Describe(x, c)));
            case "not":
                return "not " + Describe(s.Parse<CompositeConditionParams>().Of, c);
            default:
                return Humanize(s.Type);
        }
    }

    private static string Name<T>(Registry<T> registry, string id) where T : ContentDef =>
        registry.TryGet(id, out var def) ? def.DisplayName : Humanize(id);

    private static string Join(string a, string b) => a.Length == 0 ? b : b.Length == 0 ? a : a + ", " + b;

    private static string Or(IEnumerable<string> parts)
    {
        var list = parts.Where(p => p.Length > 0).ToList();
        return list.Count switch
        {
            0 => "",
            1 => list[0],
            _ => string.Join(", ", list.Take(list.Count - 1)) + " or " + list[^1],
        };
    }

    private static string Range(RangeConditionParams p, string what, string unit)
    {
        bool hasMin = p.Min > float.MinValue / 2, hasMax = p.Max < float.MaxValue / 2;
        if (hasMin && hasMax) return $"{what} {p.Min:0.##}–{p.Max:0.##}{unit}";
        if (hasMin) return $"{what} at least {p.Min:0.##}{unit}";
        if (hasMax) return $"{what} at most {p.Max:0.##}{unit}";
        return what;
    }

    /// <summary>A world depth range in words: "deep underground" past halfway to the underworld and so on.</summary>
    private static string WorldDepth(RangeConditionParams p)
    {
        bool hasMin = p.Min > float.MinValue / 2, hasMax = p.Max < float.MaxValue / 2;
        static string Way(float d) => $"{MathF.Round(d * 100f):0}% of the way to the underworld";
        if (hasMin && hasMax) return $"between {MathF.Round(p.Min * 100f):0}% and {Way(p.Max)}";
        if (hasMin) return $"deeper than {Way(p.Min)}";
        if (hasMax) return $"shallower than {Way(p.Max)}";
        return "at any depth";
    }

    private static string Hour(float h)
    {
        int hour = (int)h % 24, min = (int)MathF.Round((h - MathF.Floor(h)) * 60f) % 60;
        string ampm = hour < 12 ? "AM" : "PM";
        int h12 = hour % 12 == 0 ? 12 : hour % 12;
        return $"{h12}:{min:00} {ampm}";
    }

    /// <summary>Copper as "1 gold 50 silver".</summary>
    public static string Coins(long copper)
    {
        if (copper <= 0) return "0 copper";
        var parts = new List<string>(4);
        long p = copper / 1_000_000, g = copper / 10_000 % 100, s = copper / 100 % 100, c = copper % 100;
        if (p > 0) parts.Add($"{p} platinum");
        if (g > 0) parts.Add($"{g} gold");
        if (s > 0) parts.Add($"{s} silver");
        if (c > 0) parts.Add($"{c} copper");
        return string.Join(" ", parts);
    }

    /// <summary>"Eye of Cthulhu" → "the Eye of Cthulhu"; names that are people or plural stay bare.</summary>
    private static string Article(string name, string article = "the") =>
        name.StartsWith("The ", StringComparison.Ordinal) ? "t" + name[1..] : $"{article} {name}";

    private static string Capital(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    /// <summary>"blood_moon" → "blood moon".</summary>
    public static string Humanize(string id) => id.Replace('_', ' ').ToLower(CultureInfo.InvariantCulture);
}
