using Strata.Core.Gameplay.Items;

namespace Strata.Core.Tests.Gameplay;

/// <summary>The item use flags (merged from several slices) are distinct single bits.</summary>
public class ItemActionTests
{
    [Fact]
    public void EveryActionIsItsOwnBit()
    {
        var seen = new HashSet<int>();
        foreach (var action in Enum.GetValues<ItemAction>())
        {
            int v = (int)action;
            if (v == 0) continue;
            Assert.True((v & (v - 1)) == 0, $"{action} = {v} is not a single bit");
            Assert.True(seen.Add(v), $"{action} shares bit {v} with another action");
        }
    }
}
