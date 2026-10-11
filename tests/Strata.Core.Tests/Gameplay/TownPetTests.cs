using Strata.Core.Common;
using Strata.Core.Content.Defs;
using Strata.Core.Gameplay.Town;
using Strata.Core.Tests.Support;

namespace Strata.Core.Tests.Gameplay;

/// <summary>P5-20: licenses set their flag once, pets take a house without counting, and nothing can hurt or target them.</summary>
public class TownPetTests
{
    private static TestGame Create()
    {
        var g = TestGame.Create();
        g.World.Spawner.Enabled = false;
        g.World.Town.ArrivalsEnabled = false;
        return g;
    }

    [Fact]
    public void LicenseSetsFlagOnce()
    {
        using var g = Create();
        g.Hold("license_cat", 2);
        g.UseUntil(() => g.World.Progression.IsSet("license_cat"), maxUses: 1);
        Assert.True(g.World.Progression.IsSet("license_cat"));
        Assert.Equal(1, g.Player.State.Inventory.Backpack[0].Count);
        string? refused = null;
        g.World.ItemUse.Refused += (_, m) => refused = m;
        g.Ticks(60);
        g.UseUntil(() => false, maxUses: 1);
        Assert.Equal("This license has already been used.", refused);
        Assert.Equal(1, g.Player.State.Inventory.Backpack[0].Count); // not consumed
    }

    [Fact]
    public void PetMovesIntoHouseAndDoesNotCount()
    {
        using var g = Create();
        var town = g.World.Town;
        TestHouses.Build(g, g.Floor + new Int3(7, 0, 0));
        Assert.Null(town.CheckArrivals(force: true)); // no license yet
        g.World.Progression.Set("license_cat");
        int before = town.TownContext(g.Player).ResidentCount;
        var cat = town.CheckArrivals(force: true);
        Assert.NotNull(cat);
        Assert.Equal("town_cat", cat!.Def.Id);
        Assert.Equal(TownNpcKind.Pet, cat.Def.Kind);
        Assert.NotNull(cat.Home);
        Assert.False(cat.CountsAsResident);
        Assert.Equal(before, town.TownContext(g.Player).ResidentCount);
        Assert.EndsWith(" the Cat", cat.Title);
    }

    [Fact]
    public void PetsAreNotTargetedOrHurtAndCanBePetted()
    {
        using var g = Create();
        var town = g.World.Town;
        var node = g.World.Navigator.NearestStandable(g.Floor + new Int3(2, 1, 2), Strata.Core.Gameplay.Navigation.NavAgent.Humanoid, 3)!.Value;
        var dog = town.Summon(g.Content.TownNpcs.Get("town_dog"), node);
        town.Slay(dog);
        Assert.True(dog.Alive);
        Assert.DoesNotContain("town_dog", town.TownContext(g.Player).Residents);
        int petted = 0;
        town.Petted += (_, _) => petted++;
        Assert.True(town.PetAnimal(dog, g.Player));
        Assert.Equal(1, petted);
        Assert.False(town.PetAnimal(town.Summon(g.Content.TownNpcs.Get("guide"), node), g.Player));
    }
}
