using System.Numerics;
using Strata.Core.Common;
using Strata.Core.Gameplay.Interaction;
using Strata.Core.Gameplay.Items;
using Strata.Core.Gameplay.Wiring;
using Strata.Core.Persistence;
using Strata.Core.Tests.Support;
using Strata.Core.Voxels.TileObjects;

namespace Strata.Core.Tests.Gameplay.Wiring;

public class WiringTests
{
    private static TestGame Arena()
    {
        var g = TestGame.Create();
        g.World.Spawner.Enabled = false;
        return g;
    }

    private static TileObjectInstance Place(TestGame g, string id, Int3 at, int rotation = 0)
    {
        var obj = new TileObjectInstance(g.Content.TileObjects.Get(id), at, rotation, 0);
        Assert.True(g.World.Session.World.TileObjects.TryAdd(obj), $"{id} did not fit at {at}");
        return obj;
    }

    private static void Wire(TestGame g, Int3 from, Int3 to, WireColor color = WireColor.Red)
    {
        var min = Int3.Min(from, to);
        var max = Int3.Max(from, to);
        for (int y = min.Y; y <= max.Y; y++)
            for (int z = min.Z; z <= max.Z; z++)
                for (int x = min.X; x <= max.X; x++)
                    g.World.Wires.Place(new Int3(x, y, z), color);
    }

    [Fact]
    public void ColoursAreSeparateNetworks()
    {
        using var g = Arena();
        var f = g.Floor;
        var lever = Place(g, "lever", f + new Int3(0, 1, 0));
        var red = Place(g, "wooden_door", f + new Int3(4, 1, 0));
        var blue = Place(g, "wooden_door", f + new Int3(-4, 1, 0));
        Wire(g, f + new Int3(0, 0, 0), f + new Int3(4, 0, 0));
        Wire(g, f + new Int3(4, 1, 0), f + new Int3(4, 1, 0));
        // Blue runs through the same floor cell under the lever but towards the other door; red never reaches it.
        Wire(g, f + new Int3(-4, 0, 0), f + new Int3(0, 0, 0), WireColor.Blue);
        Wire(g, f + new Int3(-4, 1, 0), f + new Int3(-4, 1, 0), WireColor.Blue);
        Assert.Equal(WireColor.Red | WireColor.Blue, (WireColor)g.World.Wires.Grid.MaskAt(f));
        g.World.Interaction.Interact(g.Player, lever);
        Assert.True(red.Active);
        Assert.True(blue.Active);
        // Cut the blue run next to the lever: only red still reaches its door.
        g.World.Wires.Cut(f + new Int3(-1, 0, 0), WireColor.Blue);
        g.World.Interaction.Interact(g.Player, lever);
        Assert.False(red.Active);
        Assert.True(blue.Active);
    }

    [Fact]
    public void ActuatorsSwitchBlocksOffAndBackOn()
    {
        using var g = Arena();
        var f = g.Floor;
        var wall = f + new Int3(3, 1, 0);
        ushort stone = g.Content.BlockId("stone");
        g.World.Editor.SetBlock(wall, stone);
        Assert.True(g.World.Wires.PlaceActuator(wall));
        Assert.False(g.World.Wires.PlaceActuator(f + new Int3(3, 3, 0))); // no block there
        var lever = Place(g, "lever", f + new Int3(0, 1, 0));
        Wire(g, f, f + new Int3(3, 0, 0));
        Wire(g, wall, wall);
        g.World.Interaction.Interact(g.Player, lever);
        Assert.Equal(0, g.World.Session.World.GetBlock(wall));
        Assert.False(g.World.Session.World.IsSolid(wall.X, wall.Y, wall.Z));
        Assert.True(g.World.Wires.Grid.Inactive.ContainsKey(wall));
        g.World.Interaction.Interact(g.Player, lever);
        Assert.Equal(stone, g.World.Session.World.GetBlock(wall));
        Assert.Empty(g.World.Wires.Grid.Inactive);
    }

    [Fact]
    public void TimersSignalOnTheirPeriodWhileRunning()
    {
        using var g = Arena();
        var f = g.Floor;
        var timer = Place(g, "timer_1_second", f + new Int3(0, 1, 0));
        var door = Place(g, "wooden_door", f + new Int3(3, 1, 0));
        Wire(g, f, f + new Int3(3, 0, 0));
        Wire(g, f + new Int3(3, 1, 0), f + new Int3(3, 1, 0));
        g.Ticks(120);
        Assert.False(door.Active); // off until switched on
        Assert.Equal(InteractOutcome.Switched, g.World.Interaction.Interact(g.Player, timer));
        Assert.True(timer.Active);
        Assert.False(door.Active); // switching on does not signal by itself
        g.Ticks(61);
        Assert.True(door.Active);
        g.Ticks(60);
        Assert.False(door.Active);
        g.World.Interaction.Interact(g.Player, timer);
        g.Ticks(200);
        Assert.False(door.Active);
    }

    [Fact]
    public void TeleportersSwapWhoeverStandsOnThem()
    {
        using var g = Arena();
        var f = g.Floor;
        var a = Place(g, "teleporter", f + new Int3(-6, 1, -1));
        var b = Place(g, "teleporter", f + new Int3(4, 1, -1));
        Place(g, "pressure_plate", f + new Int3(-5, 2, 0)); // on pad A, wired into it from below
        Wire(g, f + new Int3(-5, 1, 0), f + new Int3(5, 1, 0), WireColor.Green); // through both pads
        Vector3? moved = null;
        g.World.Wires.Teleported += (_, feet) => moved = feet;
        g.Player.Position = TestGame.CellFeet(f + new Int3(-5, 2, 0));
        g.Ticks(1);
        Assert.NotNull(moved);
        Assert.InRange(g.Player.Position.X, (f.X + 4) * 0.5f, (f.X + 7) * 0.5f);
        Assert.Equal(1, g.World.Wires.Teleports);
    }

    [Fact]
    public void StatuesReleaseUpToTheirCap()
    {
        using var g = Arena();
        var f = g.Floor;
        var statue = Place(g, "bunny_statue", f + new Int3(2, 1, -4), rotation: 2);
        var lever = Place(g, "lever", f + new Int3(0, 1, -4));
        Wire(g, f + new Int3(0, 0, -4), f + new Int3(2, 0, -4));
        Wire(g, f + new Int3(2, 1, -4), f + new Int3(2, 1, -4));
        for (int i = 0; i < 8; i++)
        {
            g.World.Interaction.Interact(g.Player, lever);
            g.Ticks(31);
        }
        int bunnies = g.World.Npcs.All.Count(n => n.Alive && n.FromStatue);
        Assert.Equal(statue.Def.Statue!.Cap, bunnies);
        var one = g.World.Npcs.All.First(n => n.FromStatue);
        int drops = g.World.Drops.Items.Count;
        g.World.Npcs.Kill(one);
        Assert.Equal(drops, g.World.Drops.Items.Count); // statue creatures drop nothing
    }

    [Fact]
    public void LogicGatesFireWhenTheirOutputChanges()
    {
        Assert.True(WireSystem.Output("and", 2, 2));
        Assert.False(WireSystem.Output("and", 1, 2));
        Assert.True(WireSystem.Output("xor", 1, 3));
        Assert.False(WireSystem.Output("nor", 1, 2));
        using var g = Arena();
        var f = g.Floor;
        var gate = Place(g, "logic_gate_and", f + new Int3(0, 1, 4));
        var lamp1 = Place(g, "logic_gate_lamp", f + new Int3(0, 2, 4));
        var lamp2 = Place(g, "logic_gate_lamp", f + new Int3(0, 3, 4));
        var lever1 = Place(g, "lever", f + new Int3(-3, 1, 4));
        var lever2 = Place(g, "lever", f + new Int3(3, 1, 4));
        var door = Place(g, "wooden_door", f + new Int3(0, 1, 7));
        Wire(g, f + new Int3(-3, 0, 4), f + new Int3(-3, 2, 4), WireColor.Blue); // lever 1 → lamp 1
        Wire(g, f + new Int3(-2, 2, 4), f + new Int3(0, 2, 4), WireColor.Blue); // into the lamp
        Wire(g, f + new Int3(3, 0, 4), f + new Int3(3, 3, 4), WireColor.Yellow); // lever 2 → lamp 2
        Wire(g, f + new Int3(2, 3, 4), f + new Int3(0, 3, 4), WireColor.Yellow);
        Wire(g, f + new Int3(0, 0, 4), f + new Int3(0, 0, 7)); // gate → door (red)
        Wire(g, f + new Int3(0, 1, 7), f + new Int3(0, 1, 7));
        g.World.Interaction.Interact(g.Player, lever1);
        Assert.True(lamp1.Active);
        Assert.False(gate.Active);
        Assert.False(door.Active);
        g.World.Interaction.Interact(g.Player, lever2);
        Assert.True(lamp2.Active);
        Assert.True(gate.Active);
        Assert.True(door.Active);
        g.World.Interaction.Interact(g.Player, lever1);
        Assert.False(gate.Active);
        Assert.False(door.Active);
    }

    [Fact]
    public void AGateLoopStopsAfterOneFiringPerChain()
    {
        using var g = Arena();
        var f = g.Floor;
        // An XOR gate whose output wire runs back into its own lamp: each firing would flip the lamp and fire again.
        var gate = Place(g, "logic_gate_xor", f + new Int3(0, 1, 0));
        var lamp = Place(g, "logic_gate_lamp", f + new Int3(0, 2, 0));
        var lever = Place(g, "lever", f + new Int3(2, 1, 0));
        Wire(g, f + new Int3(2, 0, 0), f + new Int3(2, 2, 0));
        Wire(g, f + new Int3(1, 2, 0), f + new Int3(0, 2, 0)); // lever → lamp
        Wire(g, f + new Int3(-1, 1, 0), f + new Int3(-1, 2, 0), WireColor.Blue); // gate → its own lamp
        Wire(g, f + new Int3(0, 2, 0), f + new Int3(0, 2, 0), WireColor.Blue);
        g.World.Interaction.Interact(g.Player, lever);
        // The lever lit the lamp (gate on, fires), the gate's signal put it out again (gate off, may not fire again).
        Assert.False(lamp.Active);
        Assert.False(gate.Active);
    }

    [Fact]
    public void WiringSavesAndOldSavesLoad()
    {
        using var g = Arena();
        var grid = new WireGrid();
        grid.Add(new Int3(1, 2, 3), WireColor.Red);
        grid.Add(new Int3(1, 2, 3), WireColor.Yellow);
        grid.Add(new Int3(4, 5, 6), WireColor.Blue);
        grid.SetActuator(new Int3(7, 8, 9), true);
        grid.Inactive[new Int3(7, 8, 9)] = new InactiveBlock(g.Content.BlockId("stone"), 0, 0b0001_0001);
        var meta = new WorldMeta();
        grid.Save(meta, g.Content);
        Assert.Equal(new[] { 1, 2, 3 }, meta.Wires);
        var back = new WireGrid();
        back.Load(meta, g.Content);
        Assert.Equal(grid.Cells.OrderBy(c => c.Key.X), back.Cells.OrderBy(c => c.Key.X));
        Assert.Equal(grid.Inactive[new Int3(7, 8, 9)], back.Inactive[new Int3(7, 8, 9)]);
        Assert.Equal(2, back.WireCount);

        var old = new WorldMeta { Wires = new List<int> { 0, 0, 0, 1, 0, 0 } }; // before colours: red only, no new fields
        var oldGrid = new WireGrid();
        oldGrid.Load(old, g.Content);
        Assert.True(oldGrid.Has(new Int3(1, 0, 0), WireColor.Red));
        Assert.Equal(2, oldGrid.WireCount);
    }

    [Fact]
    public void WrenchesLayTheirColourAndTheCutterTakesEverything()
    {
        using var g = Arena();
        var cell = g.Floor + new Int3(0, 0, 3);
        g.Player.State.Inventory.Backpack[5] = new ItemStack(g.Content.Items.Get("wire"), 5);
        g.Player.State.Inventory.Backpack[6] = new ItemStack(g.Content.Items.Get("actuator"), 2);
        g.AimAt(TestGame.CellCenter(cell));
        foreach (var wrench in new[] { "red_wrench", "green_wrench", "actuator" })
        {
            g.Player.UseCooldown = 0;
            if (wrench == "actuator") g.Player.State.Inventory.Select(6);
            else g.Hold(wrench, slot: 1);
            Assert.Equal(UseOutcome.Wired, g.World.ItemUse.Use(g.Player, g.Target(), freshPress: true));
        }
        Assert.Equal(WireColor.Red | WireColor.Green, (WireColor)(g.World.Wires.Grid.MaskAt(cell) & 15));
        Assert.True(g.World.Wires.Grid.HasActuator(cell));
        Assert.Equal(3, g.Player.State.Inventory.Backpack[5].Count);
        g.Player.UseCooldown = 0;
        g.Hold("wire_cutter", slot: 1);
        Assert.Equal(UseOutcome.Wired, g.World.ItemUse.Use(g.Player, g.Target(), freshPress: true));
        Assert.Equal(0, g.World.Wires.Grid.MaskAt(cell));
        Assert.Equal(5, g.Player.State.Inventory.Backpack.Count(g.Content.Items.Get("wire")));
        Assert.Equal(2, g.Player.State.Inventory.Backpack.Count(g.Content.Items.Get("actuator")));
    }

    [Fact]
    public void StatueCreaturesDoNotWinEvents()
    {
        using var g = Strata.Core.Tests.Support.TestGame.Create();
        g.World.Spawner.Enabled = false;
        Assert.True(g.World.Events.Start(g.Content.Events.Get("slime_rain")));
        var gel = g.Content.Enemies.Get("green_slime");
        for (int i = 0; i < 100; i++)
        {
            var npc = g.World.Npcs.Spawn(gel, g.Player.Position + new System.Numerics.Vector3(3, 0, 3))!;
            npc.FromStatue = true;
            g.World.Npcs.Kill(npc);
        }
        Assert.True(g.World.Events.IsActive("slime_rain"));
    }
}
