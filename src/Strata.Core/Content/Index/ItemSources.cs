using System.Text.Json;
using Strata.Core.Content.Defs;
using Strata.Core.Content.Specs;

namespace Strata.Core.Content.Index;

/// <summary>A way an item enters the game.</summary>
public enum ItemSourceKind
{
    /// <summary>Crafted (owner: the recipe; conditions: the recipe's).</summary>
    Recipe,

    /// <summary>Dropped by an enemy or a fallen resident (owner: it; none, labelled "Any enemy", for every kill's table).</summary>
    Drop,

    /// <summary>Dropped by a boss (owner: the boss).</summary>
    BossDrop,

    /// <summary>Found in world-generated chests (owner: the loot table; label: its name, e.g. "Gold Chests").</summary>
    Container,

    /// <summary>Breaking a tile object: its loot, or the placing item of a world-generated object (owner: the object).</summary>
    Object,

    /// <summary>Mining a block (owner: the block).</summary>
    Block,

    /// <summary>Catching a critter with a bug net (owner: the critter).</summary>
    Catch,

    /// <summary>Sold by a resident (owner: the resident; conditions: when it is in stock).</summary>
    Shop,

    /// <summary>Reeled in while fishing (label: "Fishing").</summary>
    Fishing,

    /// <summary>Opening a crate or treasure bag (owner: the opened item).</summary>
    Opened,

    /// <summary>A reward for the Angler's daily quest (owner: the resident).</summary>
    AnglerReward,

    /// <summary>Filling an empty bucket at a pool (owner: the liquid).</summary>
    Bucket,

    /// <summary>Part of the starting kit or placed only by world generation (label: the tag).</summary>
    Tag,
}

/// <summary>
/// One way to obtain an item. <see cref="Min"/>–<see cref="Max"/> is the stack size; <see cref="Conditions"/> are the
/// loot entry's (outer tables first), the shop stock's or the recipe's.
/// </summary>
public sealed record ItemSource(ItemSourceKind Kind, ContentDef? Owner, string? Label, int Min, int Max, TypedSpec[] Conditions);

/// <summary>
/// Reverse lookup from an item to every way it can be obtained: recipes, enemy and boss loot, world-generated chests,
/// tile objects, blocks, bug-net catches, shops, fishing, crates and bags, Angler rewards and buckets. Loot tables are
/// resolved to their owners (nested tables inherit them). Built once from loaded content and immutable, so it is safe to
/// build off the main thread and share. The content validator's obtainability check is a loop over
/// <see cref="Unobtainable"/>.
/// </summary>
public sealed class ItemSourceIndex
{
    /// <summary>Loot table rolled for a fishing catch.</summary>
    public const string FishingTable = "fishing";

    /// <summary>Loot table rolled after every kill that counts (worth coins, not a critter or a part).</summary>
    public const string GlobalKillTable = "global_kill";

    /// <summary>Seasonal drops every slain hostile creature rolls (goodie bags, presents).</summary>
    public const string GlobalDropTable = "enemy_global_drops";

    /// <summary>An enemy also rolls table this prefix + its id, so extra drops need no edit of its own loot.</summary>
    public const string ExtraTablePrefix = "extra_";

    /// <summary>A fallen town resident drops table "&lt;resident id&gt;" + this suffix, when it exists.</summary>
    public const string DeathTableSuffix = "_death";

    /// <summary>Item tags that are a source by themselves (handed out at player creation, or placed by world generation).</summary>
    public static readonly string[] SourceTags = { "starter", "world" };

    private static readonly IReadOnlyList<ItemSource> None = Array.Empty<ItemSource>();

    private readonly Dictionary<string, List<ItemSource>> _byItem = new(StringComparer.Ordinal);
    private readonly List<ItemDef> _unobtainable = new();
    private readonly HashSet<string> _ownedTables = new(StringComparer.Ordinal);

    private ItemSourceIndex()
    {
    }

    /// <summary>True when any loot table or enemy drops coins (coin items need no other source).</summary>
    public bool DropsCoins { get; private set; }

    /// <summary>Items with no source, in registry order (debug items and, while anything drops coins, coins are exempt).</summary>
    public IReadOnlyList<ItemDef> Unobtainable => _unobtainable;

    /// <summary>Every source of the item, in a stable order (empty when none).</summary>
    public IReadOnlyList<ItemSource> For(string itemId) => _byItem.TryGetValue(itemId, out var list) ? list : None;

    public bool HasSource(string itemId) => _byItem.ContainsKey(itemId);

    /// <summary>True when something rolls the table: an owner above, or a table that does (unowned tables give nothing).</summary>
    public bool IsOwned(string tableId) => _ownedTables.Contains(tableId);

    public static ItemSourceIndex Build(ContentDatabase db)
    {
        var index = new ItemSourceIndex();
        foreach (var r in db.Recipes.All) index.Add(r.Output, new(ItemSourceKind.Recipe, r, null, r.Count, r.Count, r.Conditions));
        index.AddLoot(db);
        foreach (var block in db.Blocks.All)
            if (db.BlockDrops[block.RuntimeId] is { } drop) index.Add(drop.Id, new(ItemSourceKind.Block, block, null, 1, 1, Array.Empty<TypedSpec>()));
        foreach (var t in db.TileObjects.All)
        {
            // Breaking a world-generated object yields its placing item.
            if (t.PlacingItemId != null && (t.Natural || t.HasTag("world")))
                index.Add(t.PlacingItemId, new(ItemSourceKind.Object, t, null, 1, 1, Array.Empty<TypedSpec>()));
            // Enemy banners drop every so many kills of their creatures (BannerSystem).
            if (t.PlacingItemId != null && t.Banner != null)
                foreach (var id in t.Banner.Enemies)
                    if (db.Enemies.TryGet(id, out var enemy))
                        index.Add(t.PlacingItemId, new(ItemSourceKind.Drop, enemy, null, 1, 1, Array.Empty<TypedSpec>()));
        }
        // A bug net turns critters into their catch items.
        foreach (var e in db.Enemies.All)
            if (e.CatchItem != null) index.Add(e.CatchItem, new(ItemSourceKind.Catch, e, null, 1, 1, Array.Empty<TypedSpec>()));
        foreach (var npc in db.TownNpcs.All)
        {
            foreach (var s in npc.Shop) index.Add(s.Item, new(ItemSourceKind.Shop, npc, null, 1, 1, s.Conditions));
            foreach (var (item, count) in AnglerRewards(npc))
                index.Add(item, new(ItemSourceKind.AnglerReward, npc, null, count, count, Array.Empty<TypedSpec>()));
        }
        foreach (var i in db.Items.All)
            foreach (var tag in SourceTags)
                if (i.HasTag(tag)) index.Add(i.Id, new(ItemSourceKind.Tag, null, tag, 1, 1, Array.Empty<TypedSpec>()));
        // Filling an obtainable empty bucket yields the liquid's bucket.
        if (db.Items.All.Any(i => index.HasSource(i.Id) && i.Behaviors.Any(IsEmptyBucket)))
            foreach (var l in db.Liquids.All)
                if (l.Bucket != null) index.Add(l.Bucket, new(ItemSourceKind.Bucket, l, null, 1, 1, Array.Empty<TypedSpec>()));

        index.DropsCoins = db.LootTables.All.Any(t => t.Coins != null) || db.Enemies.All.Any(e => e.Value > 0);
        foreach (var i in db.Items.All)
        {
            if (index.HasSource(i.Id) || i.HasTag("debug") || (i.HasTag("coin") && index.DropsCoins)) continue;
            index._unobtainable.Add(i);
        }
        return index;
    }

    /// <summary>
    /// Resolves every loot table to its owners: enemies and bosses (with a worm boss's per-segment loot), fallen town
    /// residents, tile objects, items that open into a table, the fishing table and the table rolled after every kill
    /// ("Any enemy"), then named tables nothing else uses (the chests world generation fills, e.g. "Gold Chests").
    /// </summary>
    private void AddLoot(ContentDatabase db)
    {
        var claimed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var t in db.LootTables.All)
            foreach (var e in t.Entries)
                if (e.Table != null) claimed.Add(e.Table);

        void Root(string? table, ItemSourceKind kind, ContentDef? owner, string? label)
        {
            if (table == null || !db.LootTables.TryGet(table, out var def)) return;
            claimed.Add(table);
            Walk(db, def, kind, owner, label, Array.Empty<TypedSpec>(), 0);
        }

        foreach (var e in db.Enemies.All)
        {
            var kind = e.Boss != null ? ItemSourceKind.BossDrop : ItemSourceKind.Drop;
            Root(e.Loot, kind, e, null);
            Root(e.Boss?.SegmentLoot, kind, e, null);
        }
        foreach (var npc in db.TownNpcs.All) Root(npc.Id + DeathTableSuffix, ItemSourceKind.Drop, npc, null);
        // The Dye Trader's trade rolls its table for a Strange Plant (listed with the resident's wares, labelled "Trade").
        foreach (var npc in db.TownNpcs.All)
            if (npc.FindService(TownServiceSpecs.DyeTrade) is { } trade)
                Root(trade.Raw.TryGetProperty("table", out var tt) && tt.ValueKind == JsonValueKind.String ? tt.GetString() : new DyeTradeParams().Table, ItemSourceKind.Shop, npc, "Trade");
        foreach (var t in db.TileObjects.All) Root(t.Loot, ItemSourceKind.Object, t, null);
        foreach (var i in db.Items.All)
            foreach (var b in i.Behaviors)
                if (b.Type == ItemBehaviorSpecs.Consume && b.Raw.TryGetProperty("loot", out var loot) && loot.ValueKind == JsonValueKind.String)
                    Root(loot.GetString(), ItemSourceKind.Opened, i, null);
        // Fishing rolls "fishing", its lava, crate and quest tables and their "<table>_<tier>" rarities (FishingConfig).
        foreach (var t in db.LootTables.All)
            if (t.Id == FishingTable || t.Id.StartsWith(FishingTable + "_", StringComparison.Ordinal)) Root(t.Id, ItemSourceKind.Fishing, null, "Fishing");
        Root(GlobalKillTable, ItemSourceKind.Drop, null, "Any enemy");
        // Every slain hostile creature also rolls the seasonal table, and an enemy its "extra_<enemy>" table (NpcSystem.GlobalDrops).
        Root(GlobalDropTable, ItemSourceKind.Drop, null, "Any enemy");
        foreach (var e in db.Enemies.All)
            Root(ExtraTablePrefix + e.Id, e.Boss != null ? ItemSourceKind.BossDrop : ItemSourceKind.Drop, e, null);
        foreach (var t in db.LootTables.All)
            if (t.Name != null && !claimed.Contains(t.Id)) Walk(db, t, ItemSourceKind.Container, t, t.Name, Array.Empty<TypedSpec>(), 0);
    }

    private void Walk(ContentDatabase db, LootTableDef table, ItemSourceKind kind, ContentDef? owner, string? label, TypedSpec[] outer, int depth)
    {
        if (depth > LootTableDef.MaxNesting) return; // as deep as LootRoller rolls; the validator reports loot cycles
        _ownedTables.Add(table.Id);
        foreach (var e in table.Entries)
        {
            var conditions = outer.Length == 0 ? e.Conditions : e.Conditions.Length == 0 ? outer : outer.Concat(e.Conditions).ToArray();
            if (e.Item != null) Add(e.Item, new(kind, owner, label, e.Min, e.Max, conditions));
            else if (e.Table != null && db.LootTables.TryGet(e.Table, out var nested)) Walk(db, nested, kind, owner, label, conditions, depth + 1);
        }
    }

    private void Add(string? itemId, ItemSource source)
    {
        if (string.IsNullOrEmpty(itemId)) return; // a missing reference is the validator's to report
        if (!_byItem.TryGetValue(itemId, out var list)) _byItem[itemId] = list = new List<ItemSource>();
        list.Add(source);
    }

    private static bool IsEmptyBucket(TypedSpec b) => b.Type == ItemBehaviorSpecs.Bucket && !b.Raw.TryGetProperty("liquid", out _);

    /// <summary>Items an Angler-quest service hands out (malformed specs are left to the validator).</summary>
    private static IEnumerable<(string Item, int Count)> AnglerRewards(TownNpcDef npc)
    {
        if (npc.FindService(TownServiceSpecs.AnglerQuest) is not { } spec
            || !spec.Raw.TryGetProperty("rewards", out var rewards) || rewards.ValueKind != JsonValueKind.Array) yield break;
        foreach (var r in rewards.EnumerateArray())
        {
            if (r.ValueKind != JsonValueKind.Object || !r.TryGetProperty("item", out var item) || item.ValueKind != JsonValueKind.String) continue;
            int count = r.TryGetProperty("count", out var c) && c.ValueKind == JsonValueKind.Number && c.TryGetInt32(out int n) ? n : 1;
            yield return (item.GetString()!, count);
        }
    }
}
