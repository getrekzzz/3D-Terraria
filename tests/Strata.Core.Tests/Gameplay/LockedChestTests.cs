using Strata.Core.Common;
using Strata.Core.Gameplay.Interaction;
using Strata.Core.Tests.Support;
using Strata.Core.Voxels.TileObjects;

namespace Strata.Core.Tests.Gameplay;

/// <summary>Locked world chests: Golden Keys are used up, Shadow Keys kept, locked chests cannot be broken, and an
/// unopened world chest never loses its loot.</summary>
public class LockedChestTests
{
    private static TileObjectInstance Chest(TestGame g, string id, bool locked, string loot = "dungeon_chest")
    {
        var obj = new TileObjectInstance(g.Content.TileObjects.Get(id), g.Floor + new Int3(3, 1, 0), 0, 0) { Locked = locked, PendingLoot = loot };
        Assert.True(g.World.Session.World.TileObjects.TryAdd(obj));
        return obj;
    }

    private static List<string> Refusals(TestGame g)
    {
        var list = new List<string>();
        g.World.Interaction.Refused += (_, m) => list.Add(m);
        return list;
    }

    private static int Count(TestGame g, string item) => g.Player.State.Inventory.Backpack.Count(i => i.Id == item);

    [Fact]
    public void LockedChestRefusesWithoutAKey()
    {
        using var g = TestGame.Create();
        var chest = Chest(g, "gold_chest", locked: true);
        var refused = Refusals(g);
        Assert.Equal(InteractOutcome.Refused, g.World.Interaction.Interact(g.Player, chest));
        Assert.Equal(ObjectInteraction.LockedMessage, Assert.Single(refused));
        Assert.True(chest.Locked);
        Assert.False(chest.LootRolled);
    }

    [Fact]
    public void GoldenKeyIsConsumedAndTheChestOpens()
    {
        using var g = TestGame.Create();
        var chest = Chest(g, "gold_chest", locked: true);
        g.Hold("golden_key", 2, 5);
        int unlocked = 0;
        g.World.Interaction.Unlocked += (_, _) => unlocked++;
        Assert.Equal(InteractOutcome.ContainerOpened, g.World.Interaction.Interact(g.Player, chest));
        Assert.False(chest.Locked);
        Assert.Equal(1, Count(g, "golden_key"));
        Assert.Equal(1, unlocked);
        Assert.Contains(chest.Container!, s => !s.IsEmpty);
        // Once open it stays open: no second key.
        Assert.Equal(InteractOutcome.ContainerOpened, g.World.Interaction.Interact(g.Player, chest));
        Assert.Equal(1, Count(g, "golden_key"));
    }

    [Fact]
    public void ShadowKeyIsKept()
    {
        using var g = TestGame.Create();
        var chest = Chest(g, "shadow_chest", locked: true, loot: "shadow_chest");
        g.Hold("golden_key", 1, 4);
        Assert.Equal(InteractOutcome.Refused, g.World.Interaction.Interact(g.Player, chest)); // a Golden Key does not fit
        g.Hold("shadow_key", 1, 5);
        Assert.Equal(InteractOutcome.ContainerOpened, g.World.Interaction.Interact(g.Player, chest));
        Assert.Equal(1, Count(g, "shadow_key"));
        Assert.Equal(1, Count(g, "golden_key"));
        Assert.Contains(chest.Container!, s => s.Item?.Id is "sunfury" or "dark_lance" or "flamelash");
    }

    [Fact]
    public void LockedChestsCannotBeBroken()
    {
        using var g = TestGame.Create();
        var chest = Chest(g, "gold_chest", locked: true);
        var pick = g.Content.Items.Get("copper_pickaxe");
        for (int i = 0; i < 20; i++) Assert.Equal(ObjectHitResult.Locked, g.World.Editor.HitObject(chest, pick));
        Assert.Same(chest, g.World.Session.World.TileObjects.Get(chest.Id));
        Assert.False(chest.LootRolled);
    }

    [Fact]
    public void BreakingAnUnopenedWorldChestKeepsItsLoot()
    {
        using var g = TestGame.Create();
        var chest = Chest(g, "gold_chest", locked: false);
        var pick = g.Content.Items.Get("copper_pickaxe");
        // The first hit rolls the generated loot in, so the chest is not empty and stays.
        Assert.Equal(ObjectHitResult.NotEmpty, g.World.Editor.HitObject(chest, pick));
        Assert.True(chest.LootRolled);
        Assert.Contains(chest.Container!, s => !s.IsEmpty);
        Assert.NotNull(g.World.Session.World.TileObjects.Get(chest.Id));
    }

    [Fact]
    public void BiomeChestsWaitForPlantera()
    {
        using var g = TestGame.Create();
        var chest = Chest(g, "jungle_chest", locked: true, loot: "jungle_chest");
        g.Hold("jungle_key", 1, 5);
        var refused = Refusals(g);
        Assert.Equal(InteractOutcome.Refused, g.World.Interaction.Interact(g.Player, chest));
        Assert.Contains("Plantera", Assert.Single(refused));
        Assert.Equal(1, Count(g, "jungle_key"));
        g.World.Session.Flags.Add("defeated_plantera");
        Assert.Equal(InteractOutcome.ContainerOpened, g.World.Interaction.Interact(g.Player, chest));
        Assert.Equal(0, Count(g, "jungle_key"));
        Assert.Contains(chest.Container!, s => s.Item?.Id == "piranha_gun");
    }
}
