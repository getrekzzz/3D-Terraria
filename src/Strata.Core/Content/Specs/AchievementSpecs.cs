namespace Strata.Core.Content.Specs;

/// <summary>
/// Achievement triggers (also the guide's step conditions). Each reads the player's journal or the world: flags,
/// items obtained or crafted, kills, mined blocks and broken objects (journal counters), housed residents, bestiary
/// completion, residents talked to, purchases, reforges, coins carried, the layer reached and any named counter.
/// </summary>
public static class AchievementSpecs
{
    public const string Flag = "flag";
    public const string ObtainItem = "obtainItem";
    public const string CraftItem = "craftItem";
    public const string KillEnemy = "killEnemy";
    public const string MineBlock = "mineBlock";
    public const string BreakObject = "breakObject";
    public const string Housed = "housed";
    public const string Bestiary = "bestiary";
    public const string TalkTo = "talkTo";
    public const string Buy = "buy";
    public const string Reforge = "reforge";
    public const string Coins = "coins";
    public const string ReachLayer = "reachLayer";
    public const string Counter = "counter";
    public const string Rescued = "rescued";

    public static void Register(SpecCatalog c)
    {
        c.Register<FlagTrigger>(SpecKind.AchievementTrigger, Flag);
        c.Register<ItemTrigger>(SpecKind.AchievementTrigger, ObtainItem);
        c.Register<ItemTrigger>(SpecKind.AchievementTrigger, CraftItem);
        c.Register<KillTrigger>(SpecKind.AchievementTrigger, KillEnemy);
        c.Register<BreakTrigger>(SpecKind.AchievementTrigger, MineBlock);
        c.Register<BreakTrigger>(SpecKind.AchievementTrigger, BreakObject);
        c.Register<CountTrigger>(SpecKind.AchievementTrigger, Housed);
        c.Register<CountTrigger>(SpecKind.AchievementTrigger, Bestiary);
        c.Register<TalkTrigger>(SpecKind.AchievementTrigger, TalkTo);
        c.Register<CountTrigger>(SpecKind.AchievementTrigger, Buy);
        c.Register<CountTrigger>(SpecKind.AchievementTrigger, Reforge);
        c.Register<CountTrigger>(SpecKind.AchievementTrigger, Coins);
        c.Register<LayerTrigger>(SpecKind.AchievementTrigger, ReachLayer);
        c.Register<CounterTrigger>(SpecKind.AchievementTrigger, Counter);
        c.Register<RescuedTrigger>(SpecKind.AchievementTrigger, Rescued);
    }
}

/// <summary>A world flag is set (<see cref="Id"/>, and every one of <see cref="All"/>).</summary>
public sealed class FlagTrigger : SpecParams, IValidatableSpec
{
    public string? Id { get; init; }
    public string[] All { get; init; } = Array.Empty<string>();

    public string? Validate() => Id == null && All.Length == 0 ? "flag needs id or all" : null;
}

/// <summary>An item obtained or crafted: one id, any of several, any item with a tag, or any item of a category.</summary>
public sealed class ItemTrigger : SpecParams, IValidatableSpec
{
    public string? Item { get; init; }
    public string[]? Items { get; init; }
    public string? Tag { get; init; }
    public Defs.ItemCategory? Category { get; init; }

    public string? Validate() => (Item != null ? 1 : 0) + (Items != null ? 1 : 0) + (Tag != null ? 1 : 0) + (Category != null ? 1 : 0) != 1
        ? "give exactly one of item, items, tag or category" : null;
}

/// <summary>Kills of one creature, or of every creature with a tag.</summary>
public sealed class KillTrigger : SpecParams, IValidatableSpec
{
    public string? Enemy { get; init; }
    public string? Tag { get; init; }
    public int Count { get; init; } = 1;

    public string? Validate() => (Enemy == null) == (Tag == null) ? "give exactly one of enemy or tag" : Count < 1 ? "count must be >= 1" : null;
}

/// <summary>Blocks mined (<c>mineBlock</c>) or objects broken (<c>breakObject</c>): one id, a tag, or any.</summary>
public sealed class BreakTrigger : SpecParams, IValidatableSpec
{
    public string? Block { get; init; }
    public string? Object { get; init; }
    public string? Tag { get; init; }
    public int Count { get; init; } = 1;

    public string? Validate() => Block != null && Object != null ? "give block or object, not both" : Count < 1 ? "count must be >= 1" : null;
}

/// <summary>A number to reach: housed residents, bestiary percent, purchases, reforges or coins (copper).</summary>
public sealed class CountTrigger : SpecParams, IValidatableSpec
{
    public long Min { get; init; } = 1;

    public string? Validate() => Min < 1 ? "min must be >= 1" : null;
}

/// <summary>Talked to a resident (any when <see cref="Resident"/> is null).</summary>
public sealed class TalkTrigger : SpecParams
{
    public string? Resident { get; init; }
}

/// <summary>Stood in a world layer.</summary>
public sealed class LayerTrigger : SpecParams
{
    public World.WorldLayer Layer { get; init; }
}

/// <summary>Any journal counter (e.g. trees_chopped, deaths, catches) reaching a minimum.</summary>
public sealed class CounterTrigger : SpecParams, IValidatableSpec
{
    public string Key { get; init; } = "";
    public long Min { get; init; } = 1;

    public string? Validate() => Key.Length == 0 ? "key is required" : Min < 1 ? "min must be >= 1" : null;
}

/// <summary>The player freed bound townsfolk: any (<see cref="Min"/> of them) or one <see cref="Resident"/>.</summary>
public sealed class RescuedTrigger : SpecParams
{
    public string? Resident { get; init; }
    public long Min { get; init; } = 1;
}
