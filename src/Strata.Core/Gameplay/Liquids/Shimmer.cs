using System.Numerics;
using Strata.Core.Content;
using Strata.Core.Gameplay.Items;

namespace Strata.Core.Gameplay.Liquids;

/// <summary>
/// Shimmer's transmutation: an item with a <see cref="Content.Defs.ItemDef.Shimmer"/> result turns into it; anything
/// else crafted comes apart into its first recipe's ingredients (a stack decrafts as many times as it holds whole
/// batches; the remainder is left as it was).
/// </summary>
public static class Shimmer
{
    public static List<ItemStack>? Transmute(ItemStack stack, ContentDatabase content)
    {
        var item = stack.Item;
        if (item == null) return null;
        if (item.Shimmer != null && content.Items.TryGet(item.Shimmer, out var into))
            return new List<ItemStack> { new(into, stack.Count) };
        var recipe = content.Recipes.All.FirstOrDefault(r => r.Output == item.Id && r.Count <= stack.Count && r.Inputs.Length > 0);
        if (recipe == null) return null;
        int batches = stack.Count / recipe.Count;
        var result = new List<ItemStack>();
        foreach (var input in recipe.Inputs)
        {
            string? id = input.Item ?? (input.Group != null && content.RecipeGroups.TryGet(input.Group, out var g) ? g.Items.FirstOrDefault() : null);
            if (id != null && content.Items.TryGet(id, out var part)) result.Add(new ItemStack(part, input.Count * batches));
        }
        int left = stack.Count - batches * recipe.Count;
        if (left > 0) result.Add(stack.WithCount(left));
        return result.Count > 0 ? result : null;
    }
}

public sealed partial class LiquidEffects
{
    /// <summary>A dropped item in Shimmer changes once and floats up out of the pool.</summary>
    private void ShimmerDrop(DroppedItem it)
    {
        var drops = _world.Drops;
        if (!it.Shimmered)
        {
            var parts = Shimmer.Transmute(it.Stack, _content);
            if (parts != null)
            {
                drops.Remove(it);
                ItemShimmered?.Invoke(it);
                foreach (var part in parts)
                    drops.Spawn(part, it.Position, new Vector3(0, 2.5f, 0), pickupDelay: 30).Shimmered = true;
                return;
            }
            it.Shimmered = true;
        }
        it.Velocity = new Vector3(it.Velocity.X * 0.9f, 2.5f, it.Velocity.Z * 0.9f); // buoyed up and out
    }

    /// <summary>An item transmuted in Shimmer (sparkles, sound).</summary>
    public event Action<DroppedItem>? ItemShimmered;
}
