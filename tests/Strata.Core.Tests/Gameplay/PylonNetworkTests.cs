using Strata.Core.Common;
using Strata.Core.Conditions;
using Strata.Core.Gameplay.Town;
using Strata.Core.Tests.Support;

namespace Strata.Core.Tests.Gameplay;

/// <summary>P5-34: who sells which pylon, when a pylon works, and teleporting between working pylons.</summary>
public class PylonNetworkTests
{
    private static readonly Int3 West = new(-7, 0, 0), East = new(7, 0, 0);

    private static TestGame Create(bool residents = true)
    {
        var g = TestGame.Create();
        g.World.Spawner.Enabled = false;
        g.World.Town.ArrivalsEnabled = false;
        if (!residents) return g;
        TestHouses.Build(g, g.Floor + West);
        TestHouses.Build(g, g.Floor + East);
        foreach (var (id, house) in new[] { ("guide", West), ("merchant", East) })
        {
            var npc = g.World.Town.Summon(g.Content.TownNpcs.Get(id), g.World.Navigator.NearestStandable(g.Floor + new Int3(0, 1, -9), Strata.Core.Gameplay.Navigation.NavAgent.Humanoid, 3)!.Value);
            Assert.Equal(AssignResult.Assigned, g.World.Town.AssignHome(npc, g.Floor + house + new Int3(-2, 2, -2), out _));
        }
        return g;
    }

    private static Strata.Core.Voxels.TileObjects.TileObjectInstance Place(TestGame g, string id, Int3 at)
    {
        var def = g.Content.TileObjects.Get(id);
        Assert.Equal(Strata.Core.Gameplay.Building.PlacementResult.Ok, g.World.Editor.PlaceObject(def, g.Floor + at, 0));
        return g.World.Session.World.TileObjects.All.Last(o => o.Def == def);
    }

    [Fact]
    public void ResidentsSellTheirBiomesPylonWhenContentWithANeighbour()
    {
        using var g = Create();
        var town = g.World.Town;
        var guide = town.Find("guide")!;
        var forest = new ConditionContext { Biomes = new[] { "forest" }, Layer = Strata.Core.World.WorldLayer.Surface };
        Assert.Equal("forest_pylon", town.Pylons.ForSale(guide, forest, 1f)?.Id);
        Assert.Equal("jungle_pylon", town.Pylons.ForSale(guide, new ConditionContext { Biomes = new[] { "forest", "jungle" } }, 1f)?.Id);
        Assert.Equal("cavern_pylon", town.Pylons.ForSale(guide, new ConditionContext { Biomes = new[] { "forest" }, Layer = Strata.Core.World.WorldLayer.Cavern }, 1f)?.Id);
        Assert.Null(town.Pylons.ForSale(guide, forest, 1.06f)); // unhappy
        town.Dismiss(town.Find("merchant")!);
        Assert.Null(town.Pylons.ForSale(guide, forest, 1f)); // nobody lives nearby
    }

    [Fact]
    public void PylonsNeedTwoResidentsAndLinkUp()
    {
        using var g = Create();
        var town = g.World.Town;
        var a = Place(g, "forest_pylon", new Int3(0, 1, 6));
        var b = Place(g, "snow_pylon", new Int3(-4, 1, 6));
        Assert.Equal(2, town.Pylons.All().Count);
        Assert.True(town.Pylons.IsActive(a));
        Assert.Equal(PylonTeleport.SamePylon, town.Pylons.Teleport(g.Player, town.Pylons.NearbyActive(g.Player)!));
        var other = town.Pylons.NearbyActive(g.Player) == a ? b : a;
        Assert.Equal(PylonTeleport.Teleported, town.Pylons.Teleport(g.Player, other));
        Assert.Equal(1, town.Pylons.Teleports);
    }

    [Fact]
    public void WithoutResidentsPylonsDoNotWork()
    {
        using var g = Create(residents: false);
        var town = g.World.Town;
        var a = Place(g, "forest_pylon", new Int3(0, 1, 6));
        var b = Place(g, "desert_pylon", new Int3(-4, 1, 6));
        Assert.False(town.Pylons.IsActive(a));
        Assert.Null(town.Pylons.NearbyActive(g.Player));
        Assert.Equal(PylonTeleport.NotNearPylon, town.Pylons.Teleport(g.Player, b));
    }
}
