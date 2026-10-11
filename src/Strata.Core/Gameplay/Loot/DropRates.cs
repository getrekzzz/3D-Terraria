using Strata.Core.Content;
using Strata.Core.Content.Defs;

namespace Strata.Core.Gameplay.Loot;

/// <summary>One item a loot table can give, with the chance of getting it at least once per roll of the table.</summary>
public sealed record DropInfo(ItemDef Item, float Chance, int Min, int Max, TypedSpec[] Conditions);

/// <summary>
/// Drop-rate maths for loot tables, matching <see cref="LootRoller"/>: an each-mode entry drops with its chance; a
/// weighted entry with 1 − (1 − weight / total)^rolls, over every entry as if eligible (conditional entries are
/// listed with their conditions, so the exact rate depends on them); nested tables multiply through. Pure and
/// thread-safe.
/// </summary>
public static class DropRates
{
    /// <summary>Chance that a single roll of <paramref name="table"/> picks <paramref name="entry"/> at least once.</summary>
    public static float EntryChance(LootTableDef table, LootEntry entry)
    {
        if (table.Mode != LootMode.Weighted) return Math.Clamp(entry.Chance, 0f, 1f);
        float total = 0f;
        foreach (var e in table.Entries) total += Math.Max(0f, e.Weight);
        if (total <= 0f || entry.Weight <= 0f) return 0f;
        return 1f - MathF.Pow(1f - entry.Weight / total, Math.Max(0, table.Rolls));
    }

    /// <summary>Every item the table can give (nested tables flattened, an item listed once per path), in table order.</summary>
    public static List<DropInfo> For(ContentDatabase content, string tableId)
    {
        var list = new List<DropInfo>();
        if (content.LootTables.TryGet(tableId, out var table)) Walk(content, table, 1f, Array.Empty<TypedSpec>(), 0, list);
        return list;
    }

    /// <summary>Coins a table gives (copper, min–max), or null when it names none.</summary>
    public static (long Min, long Max)? Coins(LootTableDef table) => table.Coins is { Length: 2 } c ? (c[0], c[1]) : null;

    /// <summary>
    /// Coins an enemy drops (copper, min–max): its loot table's coins when the table names them, else its value ±25%
    /// (as <c>NpcSystem</c> rolls them); null when it drops none.
    /// </summary>
    public static (long Min, long Max)? Coins(ContentDatabase content, EnemyDef enemy)
    {
        if (enemy.Loot != null && content.LootTables.TryGet(enemy.Loot, out var table) && table.Coins != null) return Coins(table);
        if (enemy.Value <= 0) return null;
        return ((long)(enemy.Value * 0.75f), (long)(enemy.Value * 1.25f));
    }

    private static void Walk(ContentDatabase content, LootTableDef table, float scale, TypedSpec[] outer, int depth, List<DropInfo> into)
    {
        if (depth > LootTableDef.MaxNesting) return;
        foreach (var e in table.Entries)
        {
            float chance = scale * EntryChance(table, e);
            var conditions = outer.Length == 0 ? e.Conditions : e.Conditions.Length == 0 ? outer : outer.Concat(e.Conditions).ToArray();
            if (e.Item != null && content.Items.TryGet(e.Item, out var item)) into.Add(new DropInfo(item, chance, e.Min, e.Max, conditions));
            else if (e.Table != null && content.LootTables.TryGet(e.Table, out var nested)) Walk(content, nested, chance, conditions, depth + 1, into);
        }
    }
}
