using Strata.Core.Content;
using Strata.Core.Content.Defs;
using Strata.Core.Content.Specs;
using Strata.Core.Session;

namespace Strata.Core.Gameplay.Guide;

/// <summary>
/// A parsed achievement trigger (<see cref="AchievementSpecs"/>), also used for the guide's step conditions.
/// <see cref="Watch"/> is the journal change that can advance it; null means it reads world state and is swept.
/// </summary>
public sealed class GuideTrigger
{
    private readonly SpecParams _p;
    private readonly string? _key; // the journal counter a break, buy or counter trigger reads

    private GuideTrigger(string type, SpecParams p, JournalKind? watch, long target, string? key)
    {
        Type = type;
        _p = p;
        Watch = watch;
        Target = target;
        _key = key;
    }

    public string Type { get; }
    public JournalKind? Watch { get; }
    public long Target { get; }

    public static GuideTrigger Compile(TypedSpec spec) => spec.Type switch
    {
        AchievementSpecs.Flag => Make<FlagTrigger>(spec, null, p => 1 + p.All.Length, null),
        AchievementSpecs.ObtainItem => Make<ItemTrigger>(spec, JournalKind.Obtained, _ => 1, null),
        AchievementSpecs.CraftItem => Make<ItemTrigger>(spec, JournalKind.Crafted, _ => 1, null),
        AchievementSpecs.KillEnemy => Make<KillTrigger>(spec, JournalKind.Kill, p => p.Count, null),
        AchievementSpecs.MineBlock => Make<BreakTrigger>(spec, JournalKind.Counter, p => p.Count, p => CounterKey(JournalSystem.Mined, p.Block, p.Tag)),
        AchievementSpecs.BreakObject => Make<BreakTrigger>(spec, JournalKind.Counter, p => p.Count, p => CounterKey(JournalSystem.Broken, p.Object, p.Tag)),
        AchievementSpecs.Housed or AchievementSpecs.Bestiary or AchievementSpecs.Coins => Make<CountTrigger>(spec, null, p => p.Min, null),
        AchievementSpecs.Buy => Make<CountTrigger>(spec, JournalKind.Counter, p => p.Min, _ => JournalSystem.Purchases),
        AchievementSpecs.Reforge => Make<CountTrigger>(spec, JournalKind.Counter, p => p.Min, _ => JournalSystem.Reforges),
        AchievementSpecs.TalkTo => Make<TalkTrigger>(spec, JournalKind.Met, _ => 1, null),
        AchievementSpecs.ReachLayer => Make<LayerTrigger>(spec, null, _ => 1, null),
        AchievementSpecs.Counter => Make<CounterTrigger>(spec, JournalKind.Counter, p => p.Min, p => p.Key),
        _ => throw new InvalidOperationException($"Unknown achievement trigger '{spec.Type}'."),
    };

    private static GuideTrigger Make<T>(TypedSpec spec, JournalKind? watch, Func<T, long> target, Func<T, string>? key) where T : SpecParams
    {
        var p = spec.Parse<T>();
        if (p is FlagTrigger { Id: null } f) return new GuideTrigger(spec.Type, p, watch, f.All.Length, null);
        return new GuideTrigger(spec.Type, p, watch, target(p), key?.Invoke(p));
    }

    private static string CounterKey(string prefix, string? id, string? tag) =>
        id != null ? prefix + ":" + id : tag != null ? prefix + ":#" + tag : prefix;

    public bool Met(GameWorld world, PlayerEntity player) => Current(world, player) >= Target;

    /// <summary>Progress towards <see cref="Target"/> (capped at it). Allocation-free.</summary>
    public long Current(GameWorld world, PlayerEntity player)
    {
        var j = player.State.Journal;
        var content = world.Session.Content;
        long n = _p switch
        {
            FlagTrigger f => Flags(world, f),
            ItemTrigger i => Items(content, Type == AchievementSpecs.CraftItem ? j.Crafted : j.Obtained, i),
            KillTrigger k => Kills(content, j, k),
            CountTrigger => Type switch
            {
                AchievementSpecs.Housed => Housed(world),
                AchievementSpecs.Bestiary => (long)j.BestiaryPercent,
                AchievementSpecs.Coins => Strata.Core.Gameplay.Items.Coins.Total(player.State.Inventory),
                _ => j.Counter(_key!),
            },
            TalkTrigger t => t.Resident != null ? (j.HasMet(t.Resident) ? 1 : 0) : j.Met.Count,
            LayerTrigger l => world.Session.Generator.Plan.Bands.LayerAt(player.FeetCell.Y) == l.Layer ? 1 : 0,
            _ => j.Counter(_key!),
        };
        return Math.Min(n, Target);
    }

    private static long Flags(GameWorld world, FlagTrigger f)
    {
        long n = f.Id != null && world.Progression.IsSet(f.Id) ? 1 : 0;
        foreach (var id in f.All) if (world.Progression.IsSet(id)) n++;
        return n;
    }

    private static long Items(ContentDatabase content, HashSet<string> set, ItemTrigger t)
    {
        if (t.Item != null) return set.Contains(t.Item) ? 1 : 0;
        if (t.Items != null)
        {
            foreach (var id in t.Items) if (set.Contains(id)) return 1;
            return 0;
        }
        foreach (var id in set)
            if (content.Items.TryGet(id, out var item) && (t.Tag != null ? item.HasTag(t.Tag) : item.Category == t.Category)) return 1;
        return 0;
    }

    private static long Kills(ContentDatabase content, PlayerJournal j, KillTrigger t)
    {
        if (t.Enemy != null) return j.Kills(t.Enemy);
        long n = 0;
        foreach (var kv in j.KillCounts)
            if (content.Enemies.TryGet(kv.Key, out var e) && e.HasTag(t.Tag!)) n += kv.Value;
        return n;
    }

    private static long Housed(GameWorld world)
    {
        long n = 0;
        var residents = world.Town.Residents;
        for (int i = 0; i < residents.Count; i++)
            if (residents[i].Alive && residents[i].Home != null) n++;
        return n;
    }
}
