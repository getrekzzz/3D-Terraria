using System.Numerics;
using Strata.Core.Common;
using Strata.Core.Gameplay.Entities;
using Strata.Core.Gameplay.Interaction;
using Strata.Core.Gameplay.Items;
using Strata.Core.Gameplay.Player;
using Strata.Core.Session;
using Strata.Core.Tests.Support;
using Strata.Core.Voxels.TileObjects;

namespace Strata.Core.Tests.Gameplay;

/// <summary>The eye-plane view ray, soft-object filters, the Reach stat and the facing direction.</summary>
public class TargetingTests
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

    private static Int3 CellOf(Vector3 meters) => new(
        (int)MathF.Floor(meters.X / Units.CellSize), (int)MathF.Floor(meters.Y / Units.CellSize), (int)MathF.Floor(meters.Z / Units.CellSize));

    /// <summary>Air and no objects from the floor up to <paramref name="top"/> cells, in front of the arena centre.</summary>
    private static void ClearAbove(TestGame g, int top)
    {
        var world = g.World.Session.World;
        var batch = world.BeginEdit();
        for (int y = 1; y <= top; y++)
            for (int z = -1; z <= 10; z++)
                for (int x = -3; x <= 3; x++)
                    batch.SetBlock(g.Floor.X + x, g.Floor.Y + y, g.Floor.Z + z, 0);
        g.World.Session.Commit(batch);
        var clear = new Aabb3i(g.Floor + new Int3(-3, 1, -1), g.Floor + new Int3(4, top + 1, 11));
        foreach (var o in world.TileObjects.InBox(clear).ToList()) world.TileObjects.Remove(o);
    }

    [Fact]
    public void ViewRayIgnoresBlocksBetweenCameraAndPlayer()
    {
        using var g = Arena();
        var world = g.World.Session.World;
        var fwd = Vector3.UnitZ;
        var right = Vector3.Normalize(Vector3.Cross(fwd, Vector3.UnitY));
        var eye = g.Player.Eye;
        var camera = eye - fwd * 4.2f + right * 0.55f;
        var behind = CellOf(eye - fwd * 2f + right * 0.55f); // on the camera ray, 2 m behind the player
        SetBlock(g, behind, "stone");
        Assert.Equal(behind, Targeting.Cast(world, camera, fwd, 10f)!.Cell); // the old camera ray stops behind the player

        Assert.Null(Targeting.CastView(world, camera, fwd, eye, 2f, TargetFilter.All, out float startT));
        Assert.Equal(4.2f, startT, 3);

        int wallZ = g.Floor.Z + 7; // its face is 3.25 m ahead of the eye
        for (int y = 1; y <= 8; y++)
            for (int x = -3; x <= 3; x++)
                SetBlock(g, new Int3(g.Floor.X + x, g.Floor.Y + y, wallZ), "stone");
        var hit = Targeting.CastView(world, camera, fwd, eye, 5f, TargetFilter.All, out _);
        Assert.NotNull(hit);
        Assert.Equal(wallZ, hit.Cell.Z);
        Assert.Equal(new Int3(0, 0, -1), hit.Normal);
        Assert.InRange(hit.Distance, 3.0f, 3.6f); // from the eye, not from the camera 4.2 m further back
    }

    [Fact]
    public void DecorIsSkippedUnlessAllowed()
    {
        using var g = Arena();
        var world = g.World.Session.World;
        var tuft = new TileObjectInstance(g.Content.TileObjects.Get("grass_tuft"), g.Floor + new Int3(0, 1, 3), 0, 0);
        Assert.True(world.TileObjects.TryAdd(tuft));
        var stone = g.Floor + new Int3(0, 1, 5);
        SetBlock(g, stone, "stone");
        var origin = TestGame.CellCenter(g.Floor + Int3.Up);

        var none = Targeting.Cast(world, origin, Vector3.UnitZ, 10f, TargetFilter.None);
        Assert.Equal(stone, none!.Cell);
        Assert.Null(none.Object);
        Assert.Same(tuft, Targeting.Cast(world, origin, Vector3.UnitZ, 10f, TargetFilter.Decor)!.Object);

        // The bool overload keeps its behaviour: decor by default, looked through on request.
        Assert.Same(tuft, Targeting.Cast(world, origin, Vector3.UnitZ, 10f)!.Object);
        Assert.Equal(stone, Targeting.Cast(world, origin, Vector3.UnitZ, 10f, includeDecor: false)!.Cell);
    }

    [Fact]
    public void TreeCanopyNeedsAnAxeFilter()
    {
        using var g = Arena();
        var world = g.World.Session.World;
        ClearAbove(g, 25); // open air well above the arena for the 22-cell tree and a backstop behind its canopy

        var tree = new TileObjectInstance(g.Content.TileObjects.Get("oak_tree"), g.Floor + new Int3(-1, 1, 4), 0, 0);
        Assert.True(world.TileObjects.TryAdd(tree));
        var (min, max) = tree.TrunkBox; // the central column, 55 % of the height
        Assert.Equal(new Vector3(g.Floor.X, g.Floor.Y + 1, g.Floor.Z + 5), min);
        Assert.Equal(g.Floor.X + 1, max.X);
        Assert.Equal(g.Floor.Y + 1 + 22 * 0.55f, max.Y, 3);
        var backstop = g.Floor + new Int3(0, 18, 9);
        SetBlock(g, backstop, "stone");

        var canopy = TestGame.CellCenter(g.Floor + new Int3(0, 18, 0)); // above 55 % of the height
        var through = Targeting.Cast(world, canopy, Vector3.UnitZ, 10f, TargetFilter.None);
        Assert.Equal(backstop, through!.Cell);
        var chopped = Targeting.Cast(world, canopy, Vector3.UnitZ, 10f, TargetFilter.TreeCanopy);
        Assert.Same(tree, chopped!.Object);
        Assert.Equal(g.Floor.Z + 4, chopped.Cell.Z);

        var trunk = TestGame.CellCenter(g.Floor + new Int3(0, 3, 0));
        var atTrunk = Targeting.Cast(world, trunk, Vector3.UnitZ, 10f, TargetFilter.None);
        Assert.Same(tree, atTrunk!.Object);
        Assert.Equal(g.Floor.Z + 5, atTrunk.Cell.Z); // passes the outer cells of the box, stops at the column
        Assert.Same(tree, Targeting.Cast(world, trunk, Vector3.UnitZ, 10f, TargetFilter.TreeCanopy)!.Object);
    }

    [Fact]
    public void TrunkColumnIsHitExactlyWhereItsColliderIs()
    {
        using var g = Arena();
        var world = g.World.Session.World;
        ClearAbove(g, 31);
        for (int x = -3; x <= 3; x++) SetBlock(g, g.Floor + new Int3(x, 3, 9), "stone"); // a backstop behind the trunks
        // A ray along +Z at trunk height, x cells east of the arena centre cell's west edge.
        Target? RayAt(float x) => Targeting.Cast(world, new Vector3(g.Floor.X + x, g.Floor.Y + 3.5f, g.Floor.Z + 0.5f) * Units.CellSize, Vector3.UnitZ, 10f, TargetFilter.None);
        bool Passes(float x) => RayAt(x) is { Object: null } t && t.Cell.Z == g.Floor.Z + 9;

        // Mahogany (4 wide): the column is 1.33 cells wide, x in [-0.67, 0.67]. A ray through the outer cell's centre
        // half misses the column (and the collider), so it must pass.
        var mahogany = new TileObjectInstance(g.Content.TileObjects.Get("mahogany_tree"), g.Floor + new Int3(-2, 1, 3), 0, 0);
        Assert.True(world.TileObjects.TryAdd(mahogany));
        Assert.Equal(-2f / 3f, mahogany.TrunkBox.Min.X - g.Floor.X, 3);
        Assert.True(Passes(-0.8f));
        Assert.Same(mahogany, RayAt(-0.5f)!.Object);
        Assert.Same(mahogany, RayAt(0.6f)!.Object);
        world.TileObjects.Remove(mahogany);

        // Giant glowing mushroom (5 wide): the stem is 1.67 cells wide, x in [-0.33, 1.33], so it reaches into cells whose
        // centres lie outside it; a ray through that part must still stop at the stem.
        var mushroom = new TileObjectInstance(g.Content.TileObjects.Get("giant_glowing_mushroom"), g.Floor + new Int3(-2, 1, 3), 0, 0);
        Assert.True(world.TileObjects.TryAdd(mushroom));
        var stem = RayAt(-0.2f);
        Assert.Same(mushroom, stem!.Object);
        Assert.Equal(g.Floor.Z + 4, stem.Cell.Z); // the stem starts at z 4.67, inside cell 4
        Assert.Same(mushroom, RayAt(1.25f)!.Object);
        Assert.True(Passes(-0.45f));
        Assert.True(Passes(1.45f));
    }

    [Fact]
    public void CastWhereStopsWhereTheCallerSays()
    {
        using var g = Arena();
        var world = g.World.Session.World;
        SetBlock(g, g.Floor + new Int3(0, 1, 3), "stone");
        var dirtCell = g.Floor + new Int3(0, 1, 6);
        SetBlock(g, dirtCell, "dirt");
        ushort dirt = g.Content.BlockId("dirt");
        var origin = TestGame.CellCenter(g.Floor + Int3.Up);

        var hit = Targeting.CastWhere(world, origin, Vector3.UnitZ, 10f, (_, block) => block == dirt); // looks through the stone
        Assert.NotNull(hit);
        Assert.Equal(dirtCell, hit.Cell);
        Assert.Equal(new Int3(0, 0, -1), hit.Normal);
        Assert.Null(hit.Object);
        Assert.Equal(5.5f * Units.CellSize, hit.Distance, 3); // metres from the origin
        Assert.Null(Targeting.CastWhere(world, origin, Vector3.UnitZ, 2f, (_, block) => block == dirt));
    }

    [Fact]
    public void ReachStatExtendsReach()
    {
        using var g = Arena();
        var p = g.Player;
        var pick = g.Content.Items.Get("copper_pickaxe");
        Assert.Equal(10 * Units.CellSize, ItemUseSystem.ReachMeters(p, null), 3);
        float before = ItemUseSystem.ReachMeters(p, pick);
        Assert.Equal(pick.Reach * Units.CellSize, before, 3);

        p.State.Buffs.Add(g.Content.Buffs.Get("builder"), 600);
        p.State.Recompute();
        Assert.Equal(before + 1.0f, ItemUseSystem.ReachMeters(p, pick), 3);
    }

    [Fact]
    public void FacingQuarterFollowsViewDirection()
    {
        var p = new PlayerEntity(PlayerState.CreateNew("facing", TestPaths.Content)) { Aim = Vector3.UnitZ };
        Assert.Equal(0, p.FacingQuarter);
        p.ViewDirection = Vector3.UnitX;
        Assert.Equal(Vector3.UnitX, p.Facing);
        Assert.Equal(3, p.FacingQuarter);
        p.ViewDirection = null;
        Assert.Equal(Vector3.UnitZ, p.Facing);
        Assert.Equal(0, p.FacingQuarter);
    }

    [Fact]
    public void FilterForItems()
    {
        var db = TestPaths.Content;
        var uses = new ItemUseTable(db);
        Assert.Equal(TargetFilter.TreeCanopy | TargetFilter.Decor, ItemUseSystem.FilterFor(uses[db.Items.Get("copper_axe")]));
        Assert.Equal(TargetFilter.Decor, ItemUseSystem.FilterFor(uses[db.Items.Get("copper_pickaxe")]));
        Assert.Equal(TargetFilter.None, ItemUseSystem.FilterFor(uses[db.Items.Get("wooden_sword")]));
        Assert.Equal(TargetFilter.None, ItemUseSystem.FilterFor(uses[db.Items.Get("gray_brick")]));
        Assert.Equal(TargetFilter.None, ItemUseSystem.FilterFor(null));
    }

    [Fact]
    public void SwingArcIsCentredOnTheViewDirection()
    {
        using var g = Arena();
        var gel = g.World.Npcs.Spawn(g.Content.Enemies.Get("green_slime"), g.Player.Position + new Vector3(0, 0.05f, 1.2f))!;
        gel.Life = gel.MaxLife = 1000;
        g.Hold("wooden_sword");
        g.Player.Aim = -Vector3.UnitZ; // the aim bent away from the gel; the view still looks at it
        g.Player.ViewDirection = Vector3.UnitZ;
        var damage = new List<DamageEvent>();
        g.World.Npcs.Damaged += e => { if (!e.ToPlayer) damage.Add(e); };
        g.World.ItemUse.Use(g.Player, null, true);
        g.Ticks(40);
        Assert.Single(damage);
    }
}
