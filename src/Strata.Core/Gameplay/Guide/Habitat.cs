using Strata.Core.Conditions;
using Strata.Core.Content;
using Strata.Core.Content.Defs;
using Strata.Core.Content.Specs;

namespace Strata.Core.Gameplay.Guide;

/// <summary>Where and how a bestiary entry turns up: spawn places (from its spawn rules), events and summon items.</summary>
public sealed record HabitatInfo(IReadOnlyList<string> Places, IReadOnlyList<EventDef> Events, IReadOnlyList<ItemDef> SummonedBy);

/// <summary>
/// Describes an enemy's habitat for the bestiary from content: each spawn rule naming it (or an enemy that shares
/// its bestiary entry) reads through <see cref="ConditionDescriber"/> ("in the Surface layer, at night"), events that
/// end with it, and items whose summonBoss behaviour calls it.
/// </summary>
public static class Habitat
{
    public static HabitatInfo For(ContentDatabase content, EnemyDef enemy)
    {
        string entry = Bestiary.EntryOf(enemy);
        var places = new List<string>();
        foreach (var rule in content.SpawnRules.All)
        {
            if (!content.Enemies.TryGet(rule.Enemy, out var spawned) || Bestiary.EntryOf(spawned) != entry) continue;
            var text = ConditionDescriber.Describe(rule.Conditions, content);
            if (text.Length == 0) text = "anywhere";
            text = char.ToUpperInvariant(text[0]) + text[1..];
            if (!places.Contains(text)) places.Add(text);
        }
        var events = content.Events.All.Where(e => e.FinaleEnemy == enemy.Id).ToList();
        var summons = new List<ItemDef>();
        foreach (var item in content.Items.All)
            foreach (var b in item.Behaviors)
                if (b.Type == ItemBehaviorSpecs.SummonBoss && Summons(b, entry, content) && !summons.Contains(item)) summons.Add(item);
        return new HabitatInfo(places, events, summons);
    }

    private static bool Summons(TypedSpec spec, string entry, ContentDatabase content)
    {
        try
        {
            var id = spec.Parse<SummonBossParams>().Enemy;
            return id == entry || (content.Enemies.TryGet(id, out var e) && Bestiary.EntryOf(e) == entry);
        }
        catch (System.Text.Json.JsonException) { return false; }
    }
}
