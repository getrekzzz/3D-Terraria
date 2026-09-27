using Strata.Core.Common;
using Strata.Core.Gameplay.Interaction;
using Strata.Core.Gameplay.Items;
using Strata.Core.Tests.Support;
using Strata.Core.Voxels.TileObjects;
using Strata.Core.WorldGen.Structures;

namespace Strata.Core.Tests.Gameplay;

public class TempleTests
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
    public void PlanterasBulbStaysSealedUntilTheMechanicalBossesFall()
    {
        using var g = Arena();
        var bulb = Place(g, "plantera_bulb", 4, 4);
        var pick = g.Content.Items.Get("pickaxe_axe");
        Assert.Equal(ObjectHitResult.TooWeak, g.World.Editor.HitObject(bulb, pick));
        g.World.Progression.Set("defeated_all_mechs");
        var result = ObjectHitResult.Damaged;
        for (int i = 0; i < 10 && result != ObjectHitResult.Broken; i++) result = g.World.Editor.HitObject(bulb, pick);
        Assert.Equal(ObjectHitResult.Broken, result);
        Assert.Contains(g.World.Npcs.All, n => n.Def.Id == "plantera");
    }

    [Fact]
    public void TheTempleDoorOpensOnlyWithTheKey()
    {
        using var g = Arena();
        var door = Place(g, "lihzahrd_door", 4, 4);
        string? refused = null;
        g.World.Interaction.Refused += (_, m) => refused = m;
        Assert.Equal(InteractOutcome.Refused, g.World.Interaction.Interact(g.Player, door));
        Assert.Equal("It's locked.", refused);
        Assert.False(door.Active);
        g.Player.State.Inventory.Backpack.Add(new ItemStack(g.Content.Items.Get("temple_key"), 1));
        Assert.Equal(InteractOutcome.DoorToggled, g.World.Interaction.Interact(g.Player, door));
        Assert.True(door.Active);
        Assert.True(g.World.Progression.IsSet("temple_unlocked"));
        Assert.Equal(0, g.Player.State.Inventory.Backpack.Count(g.Content.Items.Get("temple_key")));
        // Once unlocked it is an ordinary door.
        Assert.Equal(InteractOutcome.DoorToggled, g.World.Interaction.Interact(g.Player, door));
        // No pickaxe short of a Picksaw breaks it.
        Assert.Equal(ObjectHitResult.TooWeak, g.World.Editor.HitObject(door, g.Content.Items.Get("pickaxe_axe")));
    }

    [Fact]
    public void APowerCellOnTheAltarCallsGolem()
    {
        using var g = Arena();
        var altar = Place(g, "lihzahrd_altar", 4, 4);
        Assert.Equal(InteractOutcome.Refused, g.World.Interaction.Interact(g.Player, altar));
        g.Player.State.Inventory.Backpack.Add(new ItemStack(g.Content.Items.Get("lihzahrd_power_cell"), 2));
        Assert.Equal(InteractOutcome.Activated, g.World.Interaction.Interact(g.Player, altar));
        Assert.Contains(g.World.Npcs.All, n => n.Def.Id == "golem");
        Assert.Equal(1, g.Player.State.Inventory.Backpack.Count(g.Content.Items.Get("lihzahrd_power_cell")));
        // One Golem at a time.
        Assert.Equal(InteractOutcome.None, g.World.Interaction.Interact(g.Player, altar));
    }

    [Fact]
    public void EveryJungleHidesATempleWithItsAltarAndDoor()
    {
        using var g = TestGame.Create();
        var temples = g.World.Session.Generator.Plan.Features.All.OfType<StructureFeature>().Where(f => f.Kind == "jungle_temple").ToList();
        Assert.NotEmpty(temples);
        var bp = temples[0].Blueprint;
        var objects = bp.Ops.OfType<ObjectOp>().Select(o => o.ObjectId).ToList();
        Assert.Contains("lihzahrd_altar", objects);
        Assert.Contains("lihzahrd_door", objects);
        var altar = bp.Ops.OfType<ObjectOp>().First(o => o.ObjectId == "lihzahrd_altar");
        Assert.Contains("jungle_temple", g.World.Session.ZonesAt(altar.Anchor + new Int3(0, 1, 0)));
    }
}
