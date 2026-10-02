using Strata.Core.Common;
using Strata.Core.Gameplay.Town;
using Strata.Core.Tests.Support;

namespace Strata.Core.Tests.Gameplay;

/// <summary>P5-05: housing query, assign (with swaps), and evict.</summary>
public class HousingAssignTests
{
    private static readonly Int3 West = new(-7, 0, 0), East = new(7, 0, 0);

    private static TestGame Create()
    {
        var g = TestGame.Create();
        g.World.Spawner.Enabled = false;
        g.World.Town.ArrivalsEnabled = false;
        TestHouses.Build(g, g.Floor + West);
        TestHouses.Build(g, g.Floor + East);
        return g;
    }

    private static Int3 Inside(TestGame g, Int3 house) => g.Floor + house + new Int3(-2, 2, -2);

    private static TownNpc Summon(TestGame g, string id, Int3 offset) =>
        g.World.Town.Summon(g.Content.TownNpcs.Get(id), g.World.Navigator.NearestStandable(g.Floor + offset, Strata.Core.Gameplay.Navigation.NavAgent.Humanoid, 3)!.Value);

    [Fact]
    public void AssignSwapAndEvict()
    {
        using var g = Create();
        var town = g.World.Town;
        var guide = Summon(g, "guide", new Int3(0, 1, -9));
        var merchant = Summon(g, "merchant", new Int3(2, 1, -9));

        var q = town.QueryHousing(Inside(g, West));
        Assert.True(q.Suitable);
        Assert.Null(q.Occupant);
        Assert.Equal("This housing is suitable.", q.Message);

        Assert.Equal(AssignResult.Assigned, town.AssignHome(guide, Inside(g, West), out _));
        Assert.Equal(guide, town.QueryHousing(Inside(g, West)).Occupant);
        Assert.NotNull(guide.BannerCell);
        Assert.Equal(AssignResult.AlreadyHome, town.AssignHome(guide, Inside(g, West), out _));
        Assert.Equal(AssignResult.Assigned, town.AssignHome(merchant, Inside(g, East), out _));
        var guideRoom = guide.HomeRoom;
        var merchantRoom = merchant.HomeRoom;

        // The merchant takes the guide's room; the guide gets the merchant's old one.
        Assert.Equal(AssignResult.Swapped, town.AssignHome(merchant, Inside(g, West), out _));
        Assert.Equal(guideRoom, merchant.HomeRoom);
        Assert.Equal(merchantRoom, guide.HomeRoom);

        Assert.True(town.Evict(guide));
        Assert.Null(guide.Home);
        Assert.Null(town.CheckArrivals());
        Assert.Equal(merchantRoom, guide.HomeRoom); // re-housed in the vacant room
    }

    [Fact]
    public void UnsuitableRoomsAndNonResidentsAreRefused()
    {
        using var g = Create();
        var town = g.World.Town;
        var guide = Summon(g, "guide", new Int3(0, 1, -9));
        g.World.Editor.SetBlock(g.Floor + East + new Int3(5, 3, 0), 0); // a hole in the wall
        Assert.Equal(AssignResult.NotSuitable, town.AssignHome(guide, Inside(g, East), out var why));
        Assert.Equal("This is not valid housing.", why);
        Assert.Null(guide.Home);

        var oldMan = Summon(g, "old_man", new Int3(-2, 1, -9));
        Assert.Equal(AssignResult.NotAResident, town.AssignHome(oldMan, Inside(g, West), out why));
        Assert.Contains("cannot live in a house", why);
    }
}
