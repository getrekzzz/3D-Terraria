using System.Numerics;
using Strata.Core.Common;
using Strata.Core.Gameplay.Entities;
using Strata.Core.Gameplay.Interaction;
using Strata.Core.Gameplay.Items;
using Strata.Core.Tests.Support;
using Strata.Core.Voxels.TileObjects;

namespace Strata.Core.Tests.Gameplay;

public class ObjectivesTests
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
    public void EveryThirdShadowOrbWakesTheEaterOfWorlds()
    {
        using var g = Arena();
        var messages = new List<string>();
        g.World.Objectives.Announced += (m, _) => messages.Add(m);
        for (int i = 0; i < 2; i++) g.World.Editor.BreakObject(Place(g, "shadow_orb", 3 + i * 3, 3), dropItems: true);
        Assert.Equal(2, g.World.Objectives.Count("orbs"));
        Assert.True(g.World.Progression.IsSet("smashed_orb"));
        Assert.Equal(new[] { "A horrible chill goes down your spine...", "Screams echo around you..." }, messages);
        Assert.DoesNotContain(g.World.Npcs.All, n => n.Def.Id == "eater_of_worlds");
        g.World.Editor.BreakObject(Place(g, "shadow_orb", -4, 3), dropItems: true);
        Assert.Contains(g.World.Npcs.All, n => n.Def.Id == "eater_of_worlds");
        // Each orb drops one of its treasures.
        Assert.True(g.World.Drops.Items.Count >= 3);
    }

    [Fact]
    public void AltarsNeedAPwnhammerAndSeedHardmodeOreTiers()
    {
        using var g = Arena();
        g.World.Progression.Set("defeated_wall_of_flesh");
        var altar = Place(g, "demon_altar", 3, 3);
        Assert.Equal(ObjectHitResult.TooWeak, g.World.Editor.HitObject(altar, g.Content.Items.Get("wooden_hammer")));
        var pwnhammer = g.Content.Items.Get("pwnhammer");
        var result = ObjectHitResult.Damaged;
        for (int i = 0; i < 20 && result != ObjectHitResult.Broken; i++) result = g.World.Editor.HitObject(altar, pwnhammer);
        Assert.Equal(ObjectHitResult.Broken, result);
        Assert.True(g.World.Progression.IsSet("smashed_altar_1"));
        Assert.Contains("altar_ores_1", g.World.Session.Mutations.Ids);
        Assert.DoesNotContain("altar_ores_2", g.World.Session.Mutations.Ids);
        g.World.Editor.BreakObject(Place(g, "demon_altar", -6, 3), dropItems: true);
        Assert.Contains("altar_ores_2", g.World.Session.Mutations.Ids);
    }

    [Fact]
    public void MeteoritesLandAsCratersThatBecomeTheirZone()
    {
        using var g = Arena();
        var first = g.World.Objectives.DropMeteor();
        var second = g.World.Objectives.DropMeteor();
        Assert.NotNull(first);
        Assert.NotEqual(first, second);
        Assert.Equal(2, g.World.Session.Mutations.Ids.Count(id => id == "meteorite"));
        Assert.Contains("meteorite", g.World.Session.ZonesAt(first!.Value));
        // The crater's floor is meteorite ore.
        var ore = g.Content.BlockId("meteorite");
        var data = g.World.Session.Produce(ChunkMath.CellToChunk(first.Value - new Int3(0, 5, 0))).Data;
        int count = 0;
        for (int i = 0; i < ChunkMath.Volume; i++)
            if (data.GetBlock(i) == ore) count++;
        Assert.True(count > 10, $"only {count} meteorite cells under the crater");
    }

    [Fact]
    public void TheBrainHidesBehindItsCreepersUntilTheyFall()
    {
        using var g = Arena();
        var brain = g.World.Npcs.Spawn(g.Content.Enemies.Get("brain_of_cthulhu"), g.Player.Position + new Vector3(0, 4, 6))!;
        g.Ticks(5);
        Assert.True(g.World.Npcs.All.Count(n => n.Def.Id == "creeper") > 5);
        Assert.True(brain.Invulnerable);
        Assert.False(g.World.Npcs.Hit(brain, g.Player, 50, Strata.Core.Content.Defs.DamageClass.Melee, 0, 0, g.Player.Center, melee: false));
        foreach (var creeper in g.World.Npcs.All.Where(n => n.Def.Id == "creeper").ToList()) creeper.Life = 0;
        g.Ticks(5);
        Assert.False(brain.Invulnerable);
        Assert.True(g.World.Npcs.Hit(brain, g.Player, 200, Strata.Core.Content.Defs.DamageClass.Melee, 0, 0, g.Player.Center, melee: false));
        g.Ticks(2);
        Assert.Equal("brain_of_cthulhu_exposed", brain.ModelOverride);
    }

    [Fact]
    public void TheQueenBeeDashesStingsAndCallsHerBees()
    {
        using var g = Arena();
        g.Player.State.Vitals.Life = g.Player.State.Vitals.MaxLife = 5000;
        var queen = g.World.Npcs.Spawn(g.Content.Enemies.Get("queen_bee"), g.Player.Position + new Vector3(0, 4, 8))!;
        float fastest = 0;
        bool stung = false;
        for (int t = 0; t < 900; t++)
        {
            g.World.Tick();
            fastest = MathF.Max(fastest, queen.Velocity.Length());
            stung |= g.World.Projectiles.All.Any(p => p.Def.Id == "queen_stinger");
            g.Player.State.Vitals.Life = 5000;
        }
        Assert.True(fastest > 14f, $"never dashed (top speed {fastest:0.0})");
        Assert.True(stung, "never fired a stinger");
        Assert.Contains(g.World.Npcs.All, n => n.Def.Id == "bee");
    }

    [Fact]
    public void RoyalGelMakesSlimesHarmless()
    {
        using var g = Arena();
        var p = g.Player.State;
        p.Inventory.Backpack.Add(new ItemStack(g.Content.Items.Get("royal_gel"), 1));
        for (int i = 0; i < p.Inventory.Backpack.Size; i++)
            if (p.Inventory.Backpack[i].Item?.Id == "royal_gel") p.Inventory.Equip(i);
        p.Recompute();
        var gel = g.World.Npcs.Spawn(g.Content.Enemies.Get("green_slime"), g.Player.Position + new Vector3(0, 0.05f, 0.5f))!;
        int life = p.Vitals.Life;
        g.Ticks(120);
        Assert.Equal(life, p.Vitals.Life);
        Assert.True(gel.Alive);
    }
}
