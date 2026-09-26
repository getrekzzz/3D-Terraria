using System.Numerics;
using Strata.Core.Common;
using Strata.Core.Content.Specs;
using Strata.Core.Content.Defs;
using Strata.Core.Gameplay.Entities;
using Strata.Core.Gameplay.Interaction;
using Strata.Core.Gameplay.Town;
using Strata.Core.Tests.Support;
using Strata.Core.Voxels;
using Strata.Core.WorldGen.Plan;
using Strata.Core.WorldGen.Structures;

namespace Strata.Core.Tests.Gameplay;

public class DungeonTests
{
    private const string Defeated = "defeated_skeletron";

    private static TestGame Create()
    {
        var g = TestGame.Create();
        g.World.Spawner.Enabled = false;
        g.World.Town.ArrivalsEnabled = false;
        g.Player.State.BonusLife = 100_000;
        g.Player.State.Recompute();
        g.Player.State.Vitals.Life = g.Player.State.Vitals.MaxLife;
        return g;
    }

    private static StructureFeature Crypt(TestGame g) =>
        g.World.Session.Generator.Plan.Features.All.OfType<StructureFeature>().Single(f => f.Kind == "dungeon");

    private static Int3 Gate(TestGame g) => g.World.Session.Generator.Plan.Landmarks[LandmarkIds.DungeonGate];

    /// <summary>Generates and installs the chunks around a cell (tests only stream the spawn area).</summary>
    private static void Load(TestGame g, Int3 cell, int radius = 1)
    {
        var session = g.World.Session;
        var center = ChunkMath.CellToChunk(cell);
        for (int y = -radius; y <= radius; y++)
            for (int z = -radius; z <= radius; z++)
                for (int x = -radius; x <= radius; x++)
                {
                    var c = center + new Int3(x, y, z);
                    if (session.World.ChunkInBounds(c) && !session.World.HasChunk(c)) session.Install(session.Produce(c));
                }
    }

    private static Int3 Half(Int3 v) => new(v.X / 2, v.Y / 2, v.Z / 2);

    private static long DistanceSq(Int3 a, Int3 b) => (long)(a.X - b.X) * (a.X - b.X) + (long)(a.Y - b.Y) * (a.Y - b.Y) + (long)(a.Z - b.Z) * (a.Z - b.Z);

    [Fact]
    public void ZonesFollowTheCryptsVolumesNotItsBoundingBox()
    {
        using var g = Create();
        var session = g.World.Session;
        var crypt = Crypt(g);
        var room = crypt.Blueprint.Ops.OfType<FillOp>().First().Bounds.Min;
        Assert.Contains("dungeon", session.ContextAt(room).Biomes);
        Assert.Contains(session.ContextAt(room).Biomes, b => b != "dungeon"); // the terrain biome is still reported

        // The bounding box spans open surface above the rooms: those cells are not the crypt.
        var b = crypt.Bounds;
        Int3? open = null;
        for (int x = b.Min.X; x < b.Max.X && open == null; x += 4)
            for (int z = b.Min.Z; z < b.Max.Z && open == null; z += 4)
            {
                var cell = new Int3(x, b.Max.Y - 1, z);
                if (!crypt.Blueprint.Ops.Any(op => op is not ObjectOp && op.Bounds.Contains(cell))) open = cell;
            }
        Assert.NotNull(open);
        Assert.DoesNotContain("dungeon", session.ZonesAt(open!.Value));
        Assert.DoesNotContain("dungeon", session.ZonesAt(Gate(g))); // the keeper stands outside
    }

    [Fact]
    public void CryptWallsResistPicksUntilTheWardenFalls()
    {
        using var g = Create();
        var gate = Gate(g) + new Int3(0, 0, 13);
        Load(g, gate);
        var world = g.World.Session.World;
        Int3? wall = null;
        for (int y = gate.Y; y < gate.Y + 8 && wall == null; y++)
            for (int x = gate.X - 9; x <= gate.X + 9 && wall == null; x++)
            {
                var cell = new Int3(x, y, gate.Z - 9);
                if (world.GetBlock(cell) is not 0 and not VoxelWorld.Unknown && (world.GetFlags(cell.X, cell.Y, cell.Z) & CellFlags.Structure) != 0) wall = cell;
            }
        Assert.NotNull(wall);
        var cellToMine = wall!.Value;
        Assert.True(g.World.Editor.IsLocked(cellToMine));
        var outcome = g.World.Editor.Mine(new[] { cellToMine }, 1000, 1000f, 1);
        Assert.Contains(cellToMine, outcome.TooHard);
        Assert.NotEqual(0, world.GetBlock(cellToMine));

        g.World.Session.Flags.Add(Defeated);
        Assert.False(g.World.Editor.IsLocked(cellToMine));
        g.World.Editor.Mine(new[] { cellToMine }, 1000, 1000f, 2);
        Assert.Equal(0, world.GetBlock(cellToMine));
    }

    [Fact]
    public void TheGuardianHuntsTrespassersAndCannotBeHurt()
    {
        using var g = Create();
        var content = g.Content;
        var guardian = g.World.Npcs.Spawn(content.Enemies.Get("dungeon_guardian"), g.Player.Position + new Vector3(6, 2, 0))!;
        Assert.False(g.World.Npcs.Hit(guardian, g.Player, 500, DamageClass.Melee, 0, 1f, g.Player.Center, melee: true));
        Assert.Equal(guardian.MaxLife, guardian.Life);
        // Outside the crypt it does not stay.
        g.Ticks(40);
        Assert.DoesNotContain(guardian, g.World.Npcs.All);
    }

    [Fact]
    public void InsideTheCryptOnlyItsOwnRosterSpawnsAndOneGuardianAtATime()
    {
        using var g = Create();
        var session = g.World.Session;
        // Stand in a room near the middle of the crypt.
        var crypt = Crypt(g);
        var mid = Half(crypt.Bounds.Min + crypt.Bounds.Max);
        Int3? found = null;
        foreach (var room in crypt.Blueprint.Ops.OfType<CarveOp>()
                     .Where(op => op.Bounds.Size is { X: >= 8, Z: >= 8, Y: >= 6 and <= 16 })
                     .OrderBy(op => DistanceSq(op.Bounds.Min + Half(op.Bounds.Size), mid)).Take(8))
        {
            var b = room.Bounds;
            var floor = new Int3(b.Min.X + b.Size.X / 2, b.Min.Y, b.Min.Z + b.Size.Z / 2);
            Load(g, floor, radius: 2);
            var node = g.World.Navigator.NearestStandable(floor, Strata.Core.Gameplay.Navigation.NavAgent.Humanoid, 3);
            if (node is { } n && session.ZonesAt(n).Contains("dungeon"))
            {
                found = n;
                break;
            }
        }
        Assert.NotNull(found);
        var inside = found!.Value;
        g.Player.Position = TestGame.CellFeet(inside);
        var dungeonRoster = g.Content.SpawnRules.All.Where(r => r.Conditions.Any(c => c.Type == "biome" && c.Raw.GetRawText().Contains("dungeon"))).Select(r => r.Enemy).ToHashSet();
        var spawned = new List<Npc>();
        for (int i = 0; i < 40; i++) spawned.AddRange(g.World.Spawner.TrySpawnNear(g.Player));
        var inCrypt = spawned.Where(n => session.ZonesAt(n.Cell).Contains("dungeon") || n.Def.Id == "dungeon_guardian").ToList();
        Assert.Contains(inCrypt, n => n.Def.Id != "dungeon_guardian");
        Assert.All(inCrypt, n => Assert.Contains(n.Def.Id, dungeonRoster));
        Assert.Single(g.World.Npcs.All, n => n.Def.Id == "dungeon_guardian");

        foreach (var n in g.World.Npcs.All.ToList()) g.World.Npcs.Kill(n);
        g.World.Session.Flags.Add(Defeated);
        for (int i = 0; i < 40; i++) spawned.AddRange(g.World.Spawner.TrySpawnNear(g.Player));
        Assert.DoesNotContain(g.World.Npcs.All, n => n.Def.Id == "dungeon_guardian");
    }

    [Fact]
    public void TheKeeperWaitsAtTheGateSummonsTheWardenAtNightAndLeavesWhenItFalls()
    {
        using var g = Create();
        var town = g.World.Town;
        var landmark = Gate(g);
        town.UpdateLandmarks();
        Assert.Null(town.Find("old_man")); // the gate is not loaded yet
        Load(g, landmark);
        town.UpdateLandmarks();
        var keeper = town.Find("old_man");
        Assert.NotNull(keeper);
        Assert.True(Vector3.Distance(keeper!.Position, TestGame.CellFeet(landmark)) < 5f);
        Assert.Null(keeper.Home);

        g.Player.Position = keeper.Position + new Vector3(1.5f, 0, 0);
        g.World.Session.Clock.SetHour(12f);
        Assert.Equal(TradeResult.Refused, g.World.Commerce.Summon(keeper, g.Player, out var why));
        Assert.False(string.IsNullOrEmpty(why));
        g.World.Session.Clock.SetHour(22f);
        Assert.Equal(TradeResult.Ok, g.World.Commerce.Summon(keeper, g.Player, out _));
        var boss = Assert.Single(g.World.Npcs.All, n => n.Def.Id == "skeletron");
        Assert.False(keeper.Alive); // it became the warden
        Assert.Equal(TradeResult.Unavailable, g.World.Commerce.Summon(keeper, g.Player, out _));

        var departed = new List<TownNpc>();
        town.Departed += departed.Add;
        g.World.Npcs.Kill(boss);
        Assert.Contains(Defeated, g.World.Session.Flags);
        town.UpdateLandmarks();
        Assert.Null(town.Find("old_man"));
        Assert.Single(departed);
        town.UpdateLandmarks();
        Assert.Null(town.Find("old_man")); // gone for good
    }

    [Fact]
    public void TheWardenSwipesWithAlternatingHandsSpinsAndRagesByDay()
    {
        using var g = Create();
        g.World.Session.Clock.SetHour(22f);
        var def = g.Content.Enemies.Get("skeletron");
        var p = def.Brain.Parse<CryptWardenParams>();
        var boss = g.World.Npcs.Spawn(def, g.Player.Position + new Vector3(0, 6f, 4f))!;
        Assert.Equal(2, boss.Segments.Count);
        var closest = new float[2] { float.MaxValue, float.MaxValue };
        bool spun = false;
        for (int i = 0; i < p.SpinInterval + 30; i++)
        {
            g.World.Tick();
            g.Player.State.Vitals.Life = g.Player.State.Vitals.MaxLife;
            for (int h = 0; h < 2; h++) closest[h] = MathF.Min(closest[h], Vector3.Distance(boss.Segments[h], g.Player.Center));
            Assert.True(Vector3.Distance(boss.Segments[0], boss.Center) < p.HoverHeight + p.HandSpacing * 2f + 4f);
            spun |= boss.Phase == 1;
        }
        Assert.True(spun, "never began a spin charge");
        float rest = MathF.Sqrt(p.HoverHeight * p.HoverHeight + p.HandSpacing * p.HandSpacing);
        Assert.True(closest[0] < rest - 1.5f && closest[1] < rest - 1.5f, $"both hands should swipe at the target ({closest[0]:0.0}, {closest[1]:0.0} vs rest {rest:0.0})");
        Assert.Equal(1f, boss.DamageScale);

        g.World.Session.Clock.SetHour(12f);
        g.Ticks(2);
        Assert.Equal(def.Boss!.DayEnrage!.Damage, boss.DamageScale);
        Assert.Contains(boss, g.World.Npcs.All); // it does not leave at dawn
    }

    [Fact]
    public void CastersBlinkAroundTheirTargetAndHexStopsItemUse()
    {
        using var g = Create();
        var caster = g.World.Npcs.Spawn(g.Content.Enemies.Get("dark_caster"), TestGame.CellFeet(g.Floor + new Int3(8, 1, 0)))!;
        var start = caster.Position;
        var p = caster.Def.Brain.Parse<CasterParams>();
        bool fired = false;
        for (int i = 0; i < p.TeleportInterval + 60; i++)
        {
            g.World.Tick();
            g.Player.State.Vitals.Life = g.Player.State.Vitals.MaxLife;
            fired |= g.World.Projectiles.All.Any(pr => pr.Def.Id == "water_sphere");
        }
        Assert.True(fired, "never cast");
        Assert.True(Vector3.Distance(caster.Position, start) > 1f, "never blinked");
        Assert.True(Vector3.Distance(caster.Position, g.Player.Position) <= p.MaxBlink + 1.5f);

        g.Hold("torch", 5);
        g.Player.State.Buffs.Add(g.Content.Buffs.Get("cursed"), 120);
        g.Player.State.Recompute();
        Assert.Equal(UseOutcome.Blocked, g.World.ItemUse.Use(g.Player, g.Target(), true));
    }
}
