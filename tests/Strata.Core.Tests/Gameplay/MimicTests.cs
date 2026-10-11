using System.Numerics;
using System.Text.Json;
using Strata.Core.Conditions;
using Strata.Core.World;
using Strata.Core.Content.Specs;
using Strata.Core.Content.Defs;
using Strata.Core.Gameplay.Entities;
using Strata.Core.Gameplay.Interaction;
using Strata.Core.Tests.Support;

namespace Strata.Core.Tests.Gameplay;

/// <summary>Disguised mini-bosses: the Mimic sits as a chest until opened or hit, the Lost Girl turns Nymph up close, the Wyvern flies.</summary>
public class MimicTests
{
    private static TestGame Arena()
    {
        var g = TestGame.Create();
        g.World.Spawner.Enabled = false;
        g.Player.State.BonusLife = 100_000;
        g.Player.State.Recompute();
        g.Player.State.Vitals.Life = g.Player.State.Vitals.MaxLife;
        return g;
    }

    private static Npc Spawn(TestGame g, string id, Vector3 offset) => g.World.Npcs.Spawn(g.Content.Enemies.Get(id), g.Player.Position + offset)!;

    private static float Reach(TestGame g) => ItemUseSystem.ReachMeters(g.Player, null);

    [Fact]
    public void MimicSitsAsAChestUntilOpened()
    {
        using var g = Arena();
        var mimic = Spawn(g, "mimic", new Vector3(0, 0.05f, 3f));
        int life = g.Player.State.Vitals.Life;
        g.Ticks(120);
        Assert.True(mimic.Disguised);
        Assert.Equal("mimic_chest", mimic.ShownModel);
        Assert.Equal(0f, mimic.Position.X - g.Player.Position.X, 3);
        Assert.Equal(0f, mimic.Yaw % (MathF.PI / 2), 3); // squared up like a placed chest
        Assert.Equal(life, g.Player.State.Vitals.Life);

        Assert.Equal(InteractOutcome.Unmasked, g.World.Interaction.Interact(g.Player, mimic, Reach(g)));
        Assert.False(mimic.Disguised);
        Assert.Equal("mimic", mimic.ShownModel);
        Assert.True(mimic.Velocity.Z < 0, "it springs at whoever opened it");
        // Opening it again is not a second wake.
        Assert.Equal(InteractOutcome.None, g.World.Interaction.Interact(g.Player, mimic, Reach(g)));
    }

    [Fact]
    public void AChestOutOfReachCannotBeOpened()
    {
        using var g = Arena();
        var mimic = Spawn(g, "mimic", new Vector3(0, 0.05f, 5.5f));
        g.Ticks(5);
        Assert.Equal(InteractOutcome.None, g.World.Interaction.Interact(g.Player, mimic, 2f));
        Assert.True(mimic.Disguised);
    }

    [Fact]
    public void HittingTheChestWakesIt()
    {
        using var g = Arena();
        var mimic = Spawn(g, "mimic", new Vector3(0, 0.05f, 3f));
        g.Ticks(5);
        Assert.True(g.World.Npcs.Hit(mimic, g.Player, 100, DamageClass.Melee, 0, 0f, g.Player.Center, melee: true));
        Assert.False(mimic.Disguised);
        Assert.True(mimic.Life < mimic.MaxLife);
    }

    [Fact]
    public void StandingRightBesideItWakesIt()
    {
        using var g = Arena();
        var mimic = Spawn(g, "mimic", new Vector3(0, 0.05f, 3f));
        g.Ticks(30);
        Assert.True(mimic.Disguised);
        g.Player.Position = mimic.Position + new Vector3(0, 0, -1.1f);
        g.Ticks(2);
        Assert.False(mimic.Disguised);
    }

    [Fact]
    public void TheLostGirlBecomesANymphWhenApproached()
    {
        using var g = Arena();
        var nymph = Spawn(g, "nymph", new Vector3(0, 0.05f, 5.5f));
        g.Ticks(10);
        Assert.True(nymph.Disguised);
        Assert.Equal("lost_girl", nymph.ShownModel);
        g.Player.Position = nymph.Position + new Vector3(0, 0, -3.5f);
        g.Ticks(2);
        Assert.False(nymph.Disguised);
        Assert.Equal("nymph", nymph.ShownModel);
    }

    [Fact]
    public void TheCrosshairFindsADisguisedCreatureBeforeTheWallBehindIt()
    {
        using var g = Arena();
        var mimic = Spawn(g, "mimic", new Vector3(0, 0.05f, 3f));
        g.Ticks(5);
        var eye = g.Player.Eye;
        var dir = Vector3.Normalize(mimic.Center - eye);
        Assert.Same(mimic, ObjectInteraction.DisguisedAlong(g.World.Npcs.All, eye, dir, 10f));
        Assert.Null(ObjectInteraction.DisguisedAlong(g.World.Npcs.All, eye, dir, 1f)); // terrain hit first
        Assert.Null(ObjectInteraction.DisguisedAlong(g.World.Npcs.All, eye, -dir, 10f));
        mimic.Disguised = false;
        Assert.Null(ObjectInteraction.DisguisedAlong(g.World.Npcs.All, eye, dir, 10f));
    }

    [Fact]
    public void TheWyvernFliesSweepsAndWearsLegs()
    {
        using var g = Arena();
        var def = g.Content.Enemies.Get("wyvern");
        var boss = def.Boss!;
        Assert.Equal("wyvern_legs", boss.ChainModel(0, boss.Segments, "x"));
        Assert.Equal("wyvern_body", boss.ChainModel(1, boss.Segments, "x"));
        Assert.Equal("wyvern_legs", boss.ChainModel(8, boss.Segments, "x"));
        Assert.Equal("wyvern_tail", boss.ChainModel(boss.Segments - 1, boss.Segments, "x"));
        var wyvern = Spawn(g, "wyvern", new Vector3(0, 30f, 30f));
        var speed = def.Brain.Parse<WormParams>().Speed;
        float closest = float.MaxValue;
        for (int t = 0; t < 900; t++)
        {
            g.World.Tick();
            closest = MathF.Min(closest, Vector3.Distance(wyvern.Center, g.Player.Center));
        }
        Assert.True(closest < 4f, $"it swept down on the player (closest {closest:0.0} m)");
        Assert.InRange(wyvern.Velocity.Length(), speed * 0.99f, speed * 1.61f);
        Assert.True(wyvern.Center.Y > g.Player.Center.Y - 25f, "it never falls out of the sky");
        Assert.Equal(boss.Segments, wyvern.Segments.Count);
        Assert.InRange(Vector3.Distance(wyvern.Segments[0], wyvern.Center), 0.5f, boss.SegmentSpacing + 0.01f);
    }

    [Fact]
    public void BurrowingWormsDoNotFly()
    {
        using var g = Arena();
        foreach (var id in new[] { "devourer", "bone_serpent" })
            Assert.False(g.Content.Enemies.Get(id).Brain.Parse<WormParams>().Fly);
    }
    // Biome Mimic -> the vault chest whose look its disguise copies, and the name it passes for.
    private static readonly (string Mimic, string Chest, string Name)[] BiomeMimics =
    {
        ("ice_mimic", "frozen_chest", "Frozen Chest"), ("corrupt_mimic", "corruption_chest", "Corruption Chest"),
        ("crimson_mimic", "crimson_chest", "Crimson Chest"), ("hallowed_mimic", "hallowed_chest", "Hallowed Chest"),
    };

    [Fact]
    public void BiomeMimicsSitDisguisedAsTheirBiomesChest()
    {
        using var g = Arena();
        foreach (var (id, _, name) in BiomeMimics)
        {
            var mimic = Spawn(g, id, new Vector3(0, 0.05f, 3f));
            g.Ticks(5);
            Assert.True(mimic.Disguised, id);
            Assert.Equal(id + "_chest", mimic.ShownModel);
            Assert.Equal(name, mimic.Def.Disguise!.Name);
            Assert.Equal(InteractOutcome.Unmasked, g.World.Interaction.Interact(g.Player, mimic, Reach(g)));
            Assert.Equal(id, mimic.ShownModel);
            mimic.Life = 0;
            g.Ticks(2);
        }
    }

    [Fact]
    public void EachDisguiseIsBuiltExactlyLikeItsBiomeChest()
    {
        static JsonElement Doc(string file) => JsonDocument.Parse(File.ReadAllText(Path.Combine(TestPaths.ProjectRoot, "tools", "blender", "assets.d", file))).RootElement;
        var chests = Doc("p4-34-biome-chests.json").GetProperty("objects");
        var disguises = Doc("x-11-biome-mimics.json").GetProperty("creatures");
        foreach (var (id, chest, _) in BiomeMimics)
        {
            var d = disguises.GetProperty(id + "_chest");
            Assert.Equal("mimic_chest", d.GetProperty("generator").GetString());
            Assert.Equal(chests.GetProperty(chest).GetProperty("params").GetRawText(), d.GetProperty("params").GetRawText());
        }
    }

    private static HashSet<string> Eligible(string biome, WorldLayer layer, bool hardmode)
    {
        var ctx = new ConditionContext
        {
            Biomes = new[] { biome }, Layer = layer, IsDay = true, Light = 0,
            Flags = hardmode ? new HashSet<string> { "hardmode" } : new HashSet<string>(),
        };
        return TestPaths.Content.SpawnRules.All.Where(r => ConditionCompiler.CompileAll(r.Conditions).Evaluate(ctx)).Select(r => r.Enemy).ToHashSet();
    }

    [Theory]
    [InlineData("tundra", WorldLayer.Cavern, "ice_mimic")]
    [InlineData("tundra", WorldLayer.Underground, "ice_mimic")]
    [InlineData("corruption", WorldLayer.Underground, "corrupt_mimic")]
    [InlineData("crimson", WorldLayer.Cavern, "crimson_mimic")]
    [InlineData("hallow", WorldLayer.Underground, "hallowed_mimic")]
    public void BiomeMimicsLurkUndergroundInTheirBiomeInHardmode(string biome, WorldLayer layer, string mimic)
    {
        Assert.Contains(mimic, Eligible(biome, layer, hardmode: true));
        Assert.DoesNotContain(mimic, Eligible(biome, layer, hardmode: false));
        Assert.DoesNotContain(mimic, Eligible(biome, WorldLayer.Surface, hardmode: true));
        Assert.DoesNotContain(mimic, Eligible("forest", WorldLayer.Cavern, hardmode: true));
        var rule = TestPaths.Content.SpawnRules.All.Single(r => r.Enemy == mimic);
        Assert.True(rule.Weight <= 0.12f, "rare");
        Assert.Equal(1, rule.MaxAlive);
    }

    [Fact]
    public void TheIceMimicTakesThePlainMimicsPlaceUnderTheSnow()
    {
        Assert.DoesNotContain("mimic", Eligible("tundra", WorldLayer.Cavern, hardmode: true));
        Assert.Contains("mimic", Eligible("forest", WorldLayer.Cavern, hardmode: true));
        Assert.Contains("mimic", Eligible("corruption", WorldLayer.Cavern, hardmode: true));
    }
}
