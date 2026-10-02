using Strata.Core.Conditions;
using Strata.Core.Content;
using Strata.Core.Content.Defs;
using Strata.Core.Determinism;
using Strata.Core.Gameplay.Items;

namespace Strata.Core.Gameplay.Loot;

/// <summary>Rolls loot tables (independent-chance or weighted modes, nested tables, conditional entries, coins).</summary>
public sealed class LootRoller
{
    private readonly ContentDatabase _content;
    private readonly Dictionary<LootEntry, ICondition> _conditions = new();

    public LootRoller(ContentDatabase content) => _content = content;

    public List<ItemStack> Roll(string tableId, ref Pcg32 rng, ConditionContext? context = null)
    {
        var result = new List<ItemStack>();
        RollInto(_content.LootTables.Get(tableId), ref rng, context ?? new ConditionContext(), result, 0);
        return Merge(result);
    }

    private void RollInto(LootTableDef table, ref Pcg32 rng, ConditionContext ctx, List<ItemStack> into, int depth)
    {
        if (depth > LootTableDef.MaxNesting) return;
        if (table.Coins is { Length: 2 } c) into.AddRange(Coins.ToStacks(_content, rng.Range(c[0], c[1])));
        var eligible = table.Entries.Where(e => Condition(e).Evaluate(ctx)).ToList();
        if (table.Mode == LootMode.Weighted)
        {
            float total = eligible.Sum(e => e.Weight);
            for (int r = 0; r < table.Rolls && total > 0; r++)
            {
                float pick = rng.NextFloat() * total;
                foreach (var e in eligible)
                {
                    pick -= e.Weight;
                    if (pick > 0) continue;
                    Emit(e, ref rng, ctx, into, depth);
                    break;
                }
            }
            return;
        }
        foreach (var e in eligible)
            if (rng.NextFloat() < e.Chance) Emit(e, ref rng, ctx, into, depth);
    }

    private void Emit(LootEntry e, ref Pcg32 rng, ConditionContext ctx, List<ItemStack> into, int depth)
    {
        if (e.Table != null)
        {
            RollInto(_content.LootTables.Get(e.Table), ref rng, ctx, into, depth + 1);
            return;
        }
        int count = rng.Range(e.Min, e.Max);
        if (count > 0) into.Add(new ItemStack(_content.Items.Get(e.Item!), count));
    }

    private ICondition Condition(LootEntry e)
    {
        lock (_conditions)
        {
            if (!_conditions.TryGetValue(e, out var c)) _conditions[e] = c = ConditionCompiler.CompileAll(e.Conditions);
            return c;
        }
    }

    /// <summary>Combines duplicate items and splits stacks above max size.</summary>
    private static List<ItemStack> Merge(List<ItemStack> stacks)
    {
        var result = new List<ItemStack>();
        foreach (var group in stacks.GroupBy(s => s.Item))
        {
            int total = group.Sum(s => s.Count);
            var item = group.Key!;
            while (total > 0)
            {
                int n = Math.Min(total, item.MaxStack);
                result.Add(new ItemStack(item, n));
                total -= n;
            }
        }
        return result;
    }
}
