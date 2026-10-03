using System.Numerics;
using Strata.Core.Common;
using Strata.Core.Gameplay.Entities;
using Strata.Core.Gameplay.Interaction;
using Strata.Core.Gameplay.Items;
using Strata.Core.Physics;
using Strata.Core.Tests.Support;
using Strata.Core.Voxels.TileObjects;

namespace Strata.Core.Tests.Gameplay;

public class ShotGeometryTests
{
    private static TestGame Archer()
    {
        var g = TestGame.Create();
        g.World.Spawner.Enabled = false;
        g.Hold("wooden_bow");
        g.Player.State.Inventory.Backpack[30] = new ItemStack(g.Content.Items.Get("wooden_arrow"), 20);
        return g;
    }

    /// <summary>Fires the held weapon once and returns the projectile as it spawned (position and velocity before any tick).</summary>
    private static (Vector3 Position, Vector3 Velocity) Shoot(TestGame g)
    {
        Projectile? shot = null;
        g.World.Projectiles.Spawned += p => shot ??= p;
        g.Player.ResetUse();
        Assert.Equal(UseOutcome.Shot, g.World.ItemUse.Use(g.Player, null, true));
        Assert.NotNull(shot);
        return (shot!.Position, shot.Velocity);
    }

    private static float AngleDegrees(Vector3 a, Vector3 b) =>
        MathF.Acos(Math.Clamp(Vector3.Dot(Vector3.Normalize(a), Vector3.Normalize(b)), -1f, 1f)) * 180f / MathF.PI;

    [Fact]
    public void ArrowsLeaveFromTheHand()
    {
        using var g = Archer();
        g.Player.ViewDirection = Vector3.UnitZ;
        g.Player.Aim = Vector3.UnitZ;
        g.Player.AimDistance = 10f;
        var (at, _) = Shoot(g);
        var eye = g.Player.Eye;
        // Looking along +Z the player's right is −X; a bow fires from its use style's holdout (use_styles.json).
        var h = g.World.Styles.Styles["bow"].Holdout!;
        var hand = eye + new Vector3(-h.Right, -h.Down, h.Forward);
        Assert.True(Vector3.Distance(at, eye) > 0.3f, $"spawned {Vector3.Distance(at, eye):0.00} m from the eye");
        Assert.True(Vector3.Distance(at, hand) < 0.1f, $"spawned {Vector3.Distance(at, hand):0.00} m from the hand");
    }

    [Fact]
    public void ShotsConvergeOnTheCrosshair()
    {
        using var g = Archer();
        float pitch = 10f * MathF.PI / 180f;
        g.Player.ViewDirection = Vector3.UnitZ;
        g.Player.Aim = Vector3.Normalize(new Vector3(0.15f, MathF.Sin(pitch), MathF.Cos(pitch)));
        g.Player.AimDistance = 10f;
        var (at, velocity) = Shoot(g);
        var crosshair = g.Player.Eye + g.Player.Aim * 10f;
        Assert.True(AngleDegrees(velocity, crosshair - at) < 1f, $"off the crosshair by {AngleDegrees(velocity, crosshair - at):0.00}°");
        Assert.True(AngleDegrees(velocity, g.Player.Aim) > 1f, "flies parallel to the aim instead of converging");
    }

    [Fact]
    public void CloseCrosshairPointsConvergeNoNearerThanTwoMetres()
    {
        using var g = Archer();
        g.Player.ViewDirection = Vector3.UnitZ;
        g.Player.Aim = Vector3.UnitZ;
        g.Player.AimDistance = 0.4f; // a wall right in front: converging there would fling the arrow sideways
        var (at, velocity) = Shoot(g);
        var clamped = g.Player.Eye + g.Player.Aim * 2f;
        Assert.True(AngleDegrees(velocity, clamped - at) < 1f, $"off the 2 m point by {AngleDegrees(velocity, clamped - at):0.00}°");
    }

    /// <summary>
    /// Puts the player's front against the cell row ahead and looks diagonally right, so the hand reaches past the body
    /// into the cell at the right shoulder (returned) while the eye stays in open air.
    /// </summary>
    private static Int3 HandPastTheBody(TestGame g, string style = "bow")
    {
        var front = (g.Floor.Z + 1) * Units.CellSize;
        g.Player.Position = new Vector3((g.Floor.X + 0.5f) * Units.CellSize, g.Player.Position.Y, front - 0.46f);
        var look = Vector3.Normalize(new Vector3(1f, 0, 1f));
        g.Player.ViewDirection = look;
        g.Player.Aim = look;
        g.Player.AimDistance = 10f;
        var right = Vector3.Normalize(Vector3.Cross(look, Vector3.UnitY));
        var h = g.World.Styles.Styles[style].Holdout!; // a weapon fires from its use style's holdout
        return VoxelMotion.CellOf(g.Player.Eye + right * h.Right - Vector3.UnitY * h.Down + look * h.Forward);
    }

    [Fact]
    public void NeverSpawnsInsideAWall()
    {
        using var g = Archer();
        g.Hold("flintlock_pistol"); // a gun's muzzle reaches well past the body
        g.Player.State.Inventory.Backpack[31] = new ItemStack(g.Content.Items.Get("musket_ball"), 20);
        var handCell = HandPastTheBody(g, "gun");
        var world = g.World.Session.World;
        var eye = g.Player.Eye;
        var batch = world.BeginEdit();
        for (int y = 1; y <= TestGame.ArenaHeight; y++) batch.SetBlock(handCell.X, g.Floor.Y + y, handCell.Z, g.Content.BlockId("stone"));
        g.World.Session.Commit(batch);
        Assert.True(world.IsSolid(handCell.X, handCell.Y, handCell.Z), "the hand is not inside the column");
        var eyeCell = VoxelMotion.CellOf(eye);
        Assert.False(world.IsSolid(eyeCell.X, eyeCell.Y, eyeCell.Z), "the eye is inside the column");

        var (at, velocity) = Shoot(g);
        var cell = VoxelMotion.CellOf(at);
        Assert.False(world.IsSolid(cell.X, cell.Y, cell.Z), $"spawned inside {cell}");
        Assert.True(Vector3.Distance(at, eye) < 0.01f, "a blocked hand falls back to the eye");
        Assert.True(AngleDegrees(velocity, g.Player.Aim) < 1f);
    }

    [Fact]
    public void ClosedDoorsBlockTheHandOpenOnesDoNot()
    {
        using var g = Archer();
        var handCell = HandPastTheBody(g);
        var world = g.World.Session.World;
        var door = new TileObjectInstance(g.Content.TileObjects.Get("wooden_door"), new Int3(handCell.X, g.Floor.Y + 1, handCell.Z), 0, 0);
        Assert.True(world.TileObjects.TryAdd(door));
        Assert.Same(door, world.TileObjects.At(handCell));

        // A closed door stops projectiles like a wall, so the shot starts at the eye.
        Assert.True(Vector3.Distance(ShotGeometry.Origin(world, g.Player), g.Player.Eye) < 0.01f, "a closed door let the hand through");
        // An open one does not: the shot leaves the hand.
        door.Active = true;
        Assert.True(Vector3.Distance(ShotGeometry.Origin(world, g.Player), g.Player.Eye) > 0.3f, "an open door blocked the hand");
    }

    [Fact]
    public void OpenSidesKeepTheHand()
    {
        using var g = Archer();
        var look = Vector3.Normalize(new Vector3(1f, 0, 1f));
        g.Player.ViewDirection = look;
        g.Player.Aim = look;
        var at = ShotGeometry.Origin(g.World.Session.World, g.Player);
        Assert.True(Vector3.Distance(at, g.Player.Eye) > 0.3f, "an open hand fell back to the eye");
    }
}
