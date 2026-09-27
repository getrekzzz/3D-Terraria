using Strata.Core.Common;
using Strata.Core.Gameplay.Interaction;
using Strata.Core.Gameplay.Items;
using Strata.Core.Tests.Support;
using Strata.Core.Voxels.TileObjects;

namespace Strata.Core.Tests.Gameplay;

public class AlchemyTests
{
    private static TestGame Arena()
    {
        var g = TestGame.Create();
        g.World.Spawner.Enabled = false;
        return g;
    }

    private static TileObjectInstance Place(TestGame g, string id, int dx, int dz)
    {
        var obj = new TileObjectInstance(g.Content.TileObjects.Get(id), g.Floor + new Int3(dx, 1, dz), 0, 0);
        Assert.True(g.World.Session.World.TileObjects.TryAdd(obj));
        return obj;
    }

    [Fact]
    public void ACampfireWarmsPlayersNearby()
    {
        using var g = Arena();
        Place(g, "campfire", 3, 3);
        g.Ticks(61);
        Assert.Contains(g.Player.State.Buffs.Active, b => b.Def.Id == "cozy_fire");
    }

    [Fact]
    public void LifeFruitRipensOnlyAfterAMechanicalBoss()
    {
        using var g = Arena();
        var fruit = Place(g, "life_fruit_plant", 2, 2);
        var pick = g.Content.Items.Get("pickaxe_axe");
        Assert.Equal(ObjectHitResult.TooWeak, g.World.Editor.HitObject(fruit, pick));
        g.World.Progression.Set("defeated_mechanical_boss");
        Assert.Equal(ObjectHitResult.Broken, g.World.Editor.HitObject(fruit, pick));
    }

    [Fact]
    public void PotionsAreBrewedFromBottledWaterAndHerbs()
    {
        using var g = Arena();
        foreach (var id in new[] { "swiftness_potion", "ironskin_potion", "spelunker_potion", "night_owl_potion" })
        {
            var recipe = g.Content.Recipes.All.First(r => r.Output == id);
            Assert.Contains(recipe.Inputs, i => i.Item == "bottled_water");
        }
        Assert.Contains("ores", Collect(g, "spelunker"));
        Assert.Contains("night_vision", Collect(g, "night_owl"));
    }

    [Fact]
    public void AnyMechanicalBossRaisesTheSharedFlag()
    {
        using var g = Arena();
        var destroyer = g.World.Npcs.Spawn(g.Content.Enemies.Get("the_destroyer"), g.Player.Position + new System.Numerics.Vector3(0, 30, 0))!;
        g.World.Npcs.Kill(destroyer);
        Assert.True(g.World.Progression.IsSet("defeated_the_destroyer"));
        Assert.True(g.World.Progression.IsSet("defeated_mechanical_boss"));
    }

    private static HashSet<string> Collect(TestGame g, string buffId)
    {
        var state = g.Player.State;
        state.Buffs.Add(g.Content.Buffs.Get(buffId), 600);
        state.Recompute();
        return state.Effects.Info.ToHashSet();
    }
}
