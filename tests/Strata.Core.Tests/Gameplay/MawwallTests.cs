using System.Numerics;
using Strata.Core.Common;
using Strata.Core.Content.Defs;
using Strata.Core.Content.Specs;
using Strata.Core.Gameplay.Entities;
using Strata.Core.Gameplay.Items;
using Strata.Core.Gameplay.Town;
using Strata.Core.Tests.Support;
using Strata.Core.Voxels;
using Strata.Core.World;

namespace Strata.Core.Tests.Gameplay;

public partial class MawwallTests
{
    internal static TestGame Create()
    {
        var g = TestGame.Create();
        g.World.Spawner.Enabled = false;
        g.World.Town.ArrivalsEnabled = false;
        g.Player.State.BonusLife = 100_000;
        g.Player.State.Recompute();
        g.Player.State.Vitals.Life = g.Player.State.Vitals.MaxLife;
        return g;
    }

    /// <summary>Moves the player into an open spot of the underworld cavity, with the chunks around it loaded.</summary>
    internal static Vector3 IntoUnderworld(TestGame g)
    {
        var session = g.World.Session;
        var bands = session.Generator.Plan.Bands;
        var probe = new Int3(g.Floor.X, (bands.LavaLevel + bands.UnderworldCeiling) / 2, g.Floor.Z);
        var center = ChunkMath.CellToChunk(probe);
        for (int y = -1; y <= 1; y++)
            for (int z = -1; z <= 1; z++)
                for (int x = -3; x <= 3; x++)
                {
                    var c = center + new Int3(x, y, z);
                    if (session.World.ChunkInBounds(c) && !session.World.HasChunk(c)) session.Install(session.Produce(c));
                }
        var cave = session.FindCave(probe, WorldLayer.Underworld) ?? probe;
        g.Player.Position = TestGame.CellFeet(cave);
        return g.Player.Position;
    }

    private static TownNpc Wayfinder(TestGame g)
    {
        var town = g.World.Town;
        return town.Find("guide") ?? town.Summon(g.Content.TownNpcs.Get("guide"), g.Floor + new Int3(3, 1, 0));
    }

    [Fact]
    public void DefeatingTheMawwallTurnsTheWorldToHardmode()
    {
        using var g = Create();
        var raised = new List<string>();
        g.World.Progression.Raised += f => raised.Add(f.Id);
        var wall = g.World.Npcs.Spawn(g.Content.Enemies.Get("wall_of_flesh"), g.Player.Position)!;
        g.World.Npcs.Kill(wall);
        Assert.Equal(new[] { "defeated_wall_of_flesh", "hardmode" }, raised);
        Assert.NotNull(g.Content.Flags.Get("hardmode").Announce);
        Assert.False(g.World.Progression.Set("hardmode")); // already raised
    }

    [Fact]
    public void TheEffigyOnlyCallsTheWallFromUnderworldLavaAndOnlyWhileTheWayfinderLives()
    {
        using var g = Create();
        var wayfinder = Wayfinder(g);
        var effigy = new ItemStack(g.Content.Items.Get("guide_voodoo_doll"), 1);
        var burns = g.World.Incineration;

        // Burned at the surface it is just a burned doll.
        Assert.Null(burns.Burned(new DroppedItem { Stack = effigy, Position = g.Player.Position }));
        Assert.True(wayfinder.Alive);

        var deaths = new List<TownNpc>();
        g.World.Town.Died += deaths.Add;
        var below = IntoUnderworld(g);
        var wall = burns.Burned(new DroppedItem { Stack = effigy, Position = below });
        Assert.NotNull(wall);
        Assert.Equal("wall_of_flesh", wall!.Def.Id);
        Assert.False(wayfinder.Alive);
        Assert.Single(deaths);

        // One wall at a time, and never without the wayfinder to sacrifice.
        Assert.Null(burns.Burned(new DroppedItem { Stack = effigy, Position = below }));
        g.World.Npcs.Kill(wall);
        Assert.Null(burns.Burned(new DroppedItem { Stack = effigy, Position = below }));
    }

    [Fact]
    public void LavaDestroyingTheEffigyIsWhatCallsTheWall()
    {
        using var g = Create();
        Wayfinder(g);
        var below = IntoUnderworld(g);
        var cell = Physics.VoxelMotion.CellOf(below + new Vector3(0, 0.3f, 0));
        g.World.Liquids.Enabled = false;
        g.World.Editor.FillLiquid(cell, cell, g.Content.Liquids.Get("lava"), LiquidCode.MaxLevel);
        g.World.Drops.Spawn(new ItemStack(g.Content.Items.Get("guide_voodoo_doll"), 1), below + new Vector3(0, 0.25f, 0), Vector3.Zero, pickupDelay: 600);
        g.Player.Position += new Vector3(0, 30f, 0); // out of the lava's reach
        g.Ticks(5);
        Assert.Contains(g.World.Npcs.All, n => n.Def.Id == "wall_of_flesh");
    }

    [Fact]
    public void TheWallRisesBehindItsTargetSweepsTowardsTheFarSideAndFillsTheCavity()
    {
        using var g = Create();
        var at = IntoUnderworld(g);
        var def = g.Content.Enemies.Get("wall_of_flesh");
        var p = def.Brain.Parse<MawwallParams>();
        var wall = g.World.Npcs.Spawn(def, at)!;
        g.World.Tick();
        float worldX = g.World.Session.World.SizeCells.X * Units.CellSize;
        float dir = at.X < worldX * 0.5f ? 1f : -1f;
        Assert.Equal(dir, wall.Direction.X);
        Assert.Equal(at.X - dir * p.StartDistance, wall.Center.X, 1);
        var bands = g.World.Session.Generator.Plan.Bands;
        var (min, max) = wall.Barrier!.Value;
        Assert.True(min.Y <= (bands.LavaLevel + 1) * Units.CellSize + 0.01f && max.Y >= bands.UnderworldCeiling * Units.CellSize - 0.01f);
        Assert.Equal(p.Width, max.Z - min.Z, 2);
        Assert.Equal(2, wall.Segments.Count);
        Assert.True(wall.Segments.All(s => (s.X - wall.Center.X) * dir > 0), "eyes sit on the front face");

        float before = wall.Center.X;
        g.Ticks(120);
        Assert.InRange((wall.Center.X - before) * dir, p.BaseSpeed * 1.5f, p.BaseSpeed * 2.5f);

        // Hurt, it hurries.
        wall.Life = wall.MaxLife / 10;
        before = wall.Center.X;
        g.Ticks(60);
        Assert.True((wall.Center.X - before) * dir > p.BaseSpeed * 2f);
    }

    [Fact]
    public void ThePlayerCannotHideBehindTheWall()
    {
        using var g = Create();
        var at = IntoUnderworld(g);
        var wall = g.World.Npcs.Spawn(g.Content.Enemies.Get("wall_of_flesh"), at)!;
        g.World.Tick();
        float dir = wall.Direction.X;
        var knocks = new List<Vector3>();
        g.World.Npcs.PlayerHurt += (_, _, knock) => knocks.Add(knock);
        g.Player.Position = new Vector3(wall.Center.X - dir * 6f, at.Y, wall.Center.Z + 3f);
        int life = g.Player.State.Vitals.Life;
        g.Ticks(2);
        Assert.True(g.Player.State.Vitals.Life < life);
        Assert.Contains(knocks, k => k.X * dir > 5f); // thrown out in front
    }

    [Fact]
    public void EyesFireAndTheMouthSpitsTetheredMinionsThatDieWithIt()
    {
        using var g = Create();
        var at = IntoUnderworld(g);
        var def = g.Content.Enemies.Get("wall_of_flesh");
        var p = def.Brain.Parse<MawwallParams>();
        var wall = g.World.Npcs.Spawn(def, at + new Vector3(0, 0, 0))!;
        g.World.Tick();
        // Stand close in front of the eyes.
        g.Player.Position = new Vector3(wall.Center.X + wall.Direction.X * 12f, wall.Center.Y - 1f, wall.Center.Z);
        var shooters = new HashSet<int>();
        for (int i = 0; i < p.MinionInterval + 5; i++)
        {
            g.World.Tick();
            g.Player.State.Vitals.Life = g.Player.State.Vitals.MaxLife;
            g.Player.Position = new Vector3(wall.Center.X + wall.Direction.X * 12f, wall.Center.Y - 1f, wall.Center.Z);
            foreach (var shot in g.World.Projectiles.All.Where(s => s.Def.Id == "wall_of_flesh_laser" && s.Age <= 1))
                shooters.Add(Vector3.Distance(shot.Position, wall.Segments[0]) < Vector3.Distance(shot.Position, wall.Segments[1]) ? 0 : 1);
        }
        Assert.Equal(2, shooters.Count); // both eyes take turns
        var minion = Assert.Single(wall.Followers);
        Assert.Equal("the_hungry", minion.Def.Id);
        Assert.Same(wall, minion.Leader);

        g.World.Npcs.Kill(wall);
        g.World.Tick();
        Assert.DoesNotContain(minion, g.World.Npcs.All);
    }

    [Fact]
    public void TheWallLeavesIfItsTargetFleesTheUnderworld()
    {
        using var g = Create();
        var at = IntoUnderworld(g);
        var wall = g.World.Npcs.Spawn(g.Content.Enemies.Get("wall_of_flesh"), at)!;
        var left = new List<Npc>();
        g.World.Npcs.BossLeft += left.Add;
        g.Ticks(40);
        Assert.Contains(wall, g.World.Npcs.All);
        g.Player.Position = TestGame.CellFeet(g.Floor + Int3.Up);
        g.Ticks(40);
        Assert.DoesNotContain(wall, g.World.Npcs.All);
        Assert.Single(left);
        Assert.DoesNotContain("defeated_wall_of_flesh", g.World.Session.Flags);
    }
}
