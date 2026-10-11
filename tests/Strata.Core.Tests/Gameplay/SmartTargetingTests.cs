using System.Numerics;
using Strata.Core.Common;
using Strata.Core.Gameplay.Interaction;
using Strata.Core.Gameplay.Items;
using Strata.Core.Tests.Support;
using Strata.Core.Voxels.TileObjects;

namespace Strata.Core.Tests.Gameplay;

/// <summary>Smart Cursor (P2-29): a pickaxe snaps to a minable block near the aim, an axe to the nearest tree.</summary>
public class SmartTargetingTests
{
    private static TestGame Arena()
    {
        var g = TestGame.Create();
        g.World.Spawner.Enabled = false;
        return g;
    }

    private static void SetBlock(TestGame g, Int3 cell, string? block)
    {
        var batch = g.World.Session.World.BeginEdit();
        batch.SetBlock(cell.X, cell.Y, cell.Z, block == null ? (ushort)0 : g.Content.BlockId(block));
        g.World.Session.Commit(batch);
    }

    private static ItemUseInfo Tool(TestGame g, string id) => g.World.Uses[g.Content.Items.Get(id)];

    /// <summary>Finds with the tool's own powers.</summary>
    private static Target? Find(TestGame g, Vector3 eye, Vector3 aim, float reach, string tool)
    {
        var info = Tool(g, tool);
        return SmartTargeting.Find(g.World.Session.World, eye, aim, reach, info, info.Item.PickPower, info.Item.AxePower, info.Item.HammerPower);
    }

    /// <summary>The eye four cells over the arena floor, looking along +Z down the open arena.</summary>
    private static Vector3 Eye(TestGame g) => TestGame.CellCenter(g.Floor + new Int3(0, 4, 0));

    [Fact]
    public void FindsOreBesideTheRay()
    {
        using var g = Arena();
        var world = g.World.Session.World;
        var ore = g.Floor + new Int3(1, 4, 5); // its centre 0.5 m beside the aim line, 2.5 m ahead
        SetBlock(g, ore, "copper_ore");
        var eye = Eye(g);
        Assert.Null(Targeting.Cast(world, eye, Vector3.UnitZ, 5f, TargetFilter.Decor)); // the plain crosshair misses it

        var hit = Find(g, eye, Vector3.UnitZ, 5f, "copper_pickaxe");
        Assert.NotNull(hit);
        Assert.Equal(ore, hit.Cell);
        Assert.Equal(g.Content.BlockId("copper_ore"), hit.Block);
        Assert.Equal(new Int3(0, 0, -1), hit.Normal); // the face toward the eye
        Assert.InRange(hit.Distance, 2.2f, 2.5f);

        // An ore in the cone beats a plain block nearer along the aim (Terraria's smart cursor goes for shinies).
        SetBlock(g, g.Floor + new Int3(-1, 4, 3), "dirt");
        Assert.Equal(ore, Find(g, eye, Vector3.UnitZ, 5f, "copper_pickaxe")!.Cell);
        // An axe has no use for blocks.
        Assert.Null(Find(g, eye, Vector3.UnitZ, 5f, "copper_axe"));
    }

    [Fact]
    public void IgnoresBlocksTooHardForThePick()
    {
        using var g = Arena();
        var obsidian = g.Floor + new Int3(0, 4, 5); // on the aim line, pick power 55
        var dirt = g.Floor + new Int3(1, 4, 5);
        SetBlock(g, obsidian, "obsidian");
        SetBlock(g, dirt, "dirt");
        var eye = Eye(g);
        var info = Tool(g, "copper_pickaxe"); // pick power 35

        Assert.Equal(dirt, SmartTargeting.Find(g.World.Session.World, eye, Vector3.UnitZ, 5f, info, 35, 0, 0)!.Cell);
        // Strong enough, the block right on the aim line wins over the one beside it.
        Assert.Equal(obsidian, SmartTargeting.Find(g.World.Session.World, eye, Vector3.UnitZ, 5f, info, 65, 0, 0)!.Cell);
    }

    [Fact]
    public void BuriedBlocksAreNeverPicked()
    {
        using var g = Arena();
        // A 3×3×3 dirt cube round an ore: only the dirt faces toward the eye are open.
        var centre = g.Floor + new Int3(0, 4, 6);
        for (int y = -1; y <= 1; y++)
            for (int z = -1; z <= 1; z++)
                for (int x = -1; x <= 1; x++)
                    SetBlock(g, centre + new Int3(x, y, z), x == 0 && y == 0 && z == 0 ? "copper_ore" : "dirt");

        var hit = Find(g, Eye(g), Vector3.UnitZ, 5f, "copper_pickaxe");
        Assert.NotNull(hit);
        Assert.Equal(centre + new Int3(0, 0, -1), hit.Cell); // the dirt in front, not the ore inside
    }

    [Fact]
    public void ChopsNearestTreeInCone()
    {
        using var g = Arena();
        var world = g.World.Session.World;
        // Near tree beside the aim line (its box 0.25 m off it), far tree right on it.
        var near = new TileObjectInstance(g.Content.TileObjects.Get("oak_tree"), g.Floor + new Int3(1, 1, 5), 0, 0);
        var far = new TileObjectInstance(g.Content.TileObjects.Get("oak_tree"), g.Floor + new Int3(-1, 1, 9), 0, 0);
        Assert.True(world.TileObjects.TryAdd(near));
        Assert.True(world.TileObjects.TryAdd(far));
        var eye = Eye(g);
        Assert.Same(far, Targeting.Cast(world, eye, Vector3.UnitZ, 5f, TargetFilter.All)!.Object);

        var hit = Find(g, eye, Vector3.UnitZ, 5f, "copper_axe");
        Assert.NotNull(hit);
        Assert.Same(near, hit.Object);
        Assert.True(hit.Distance <= 5f);
        // A pickaxe does not chop: nothing it can mine is in the cone.
        Assert.Null(Find(g, eye, Vector3.UnitZ, 5f, "copper_pickaxe"));
    }

    [Fact]
    public void NothingBeyondReach()
    {
        using var g = Arena();
        var ore = g.Floor + new Int3(0, 4, 12); // its near face 5.75 m from the eye
        SetBlock(g, ore, "copper_ore");
        var eye = Eye(g);

        Assert.Null(Find(g, eye, Vector3.UnitZ, 5f, "copper_pickaxe"));
        var hit = Find(g, eye, Vector3.UnitZ, 6f, "copper_pickaxe");
        Assert.Equal(ore, hit!.Cell);
        Assert.True(hit.Distance <= 6f);
    }
}
