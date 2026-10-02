using Strata.Core.Content.Defs;
using Strata.Core.Persistence;
using Strata.Core.Tests.Support;
using Strata.Core.World;
using Strata.Core.WorldGen;
using Strata.Core.WorldGen.Features;
using Strata.Core.WorldGen.Structures;
using Strata.Core.WorldGen.Structures.Dungeon;

namespace Strata.Core.Tests.WorldGen;

/// <summary>Secret world seeds (X-07): names, saving, and what each changes (only when set).</summary>
public class SecretSeedTests
{
    [Theory]
    [InlineData("for the worthy", SecretSeed.ForTheWorthy)]
    [InlineData("For The Worthy!", SecretSeed.ForTheWorthy)]
    [InlineData("05162020", SecretSeed.Drunk)]
    [InlineData("drunk world", SecretSeed.Drunk)]
    [InlineData("not the bees!!", SecretSeed.NotTheBees)]
    [InlineData("Don't dig up", SecretSeed.DontDigUp)]
    [InlineData("the constant", SecretSeed.TheConstant)]
    [InlineData("celebrationmk10", SecretSeed.Celebration)]
    [InlineData("no traps", SecretSeed.NoTraps)]
    [InlineData("get fixed boi", SecretSeed.GetFixedBoi)]
    [InlineData("12345", SecretSeed.None)]
    [InlineData("my world", SecretSeed.None)]
    [InlineData("", SecretSeed.None)]
    public void NamesAreRecognised(string text, SecretSeed expected) => Assert.Equal(expected, SecretSeeds.Parse(text));

    [Fact]
    public void FlagsSurviveSaving()
    {
        var all = SecretSeed.GetFixedBoi;
        Assert.Equal(all, SecretSeeds.FromIds(SecretSeeds.Ids(all)));
        var meta = new WorldMeta { Secrets = SecretSeeds.Ids(SecretSeed.Drunk | SecretSeed.NoTraps) };
        Assert.Equal(SecretSeed.Drunk | SecretSeed.NoTraps, meta.ToSettings().Secret);
        Assert.Equal(SecretSeed.None, new WorldMeta().ToSettings().Secret);
        var dir = TestWorlds.TempDir();
        using (var g = TestGame.Create(dir, secret: SecretSeed.Celebration)) g.World.SaveAll();
        var session = Strata.Core.Session.WorldSession.Load(TestPaths.Content, dir, new Strata.Core.Meshing.RenderTables(TestPaths.Content, _ => 0),
            new Strata.Core.Diagnostics.MemoryLog());
        Assert.True(session.Settings.Has(SecretSeed.Celebration));
    }

    private static WorldGenerator Gen(SecretSeed secret, long seed = 1234) =>
        new(TestPaths.Content, new WorldSettings { Seed = seed, Size = WorldSizePreset.Tiny, Secret = secret });

    [Fact]
    public void OrdinaryWorldsAreUnchangedBySecretSeedCode()
    {
        // Flags that only change gameplay leave the generated world as it was.
        Assert.Equal(Gen(SecretSeed.None).Plan.Hash, Gen(SecretSeed.ForTheWorthy | SecretSeed.TheConstant | SecretSeed.Celebration).Plan.Hash);
        Assert.Equal(Gen(SecretSeed.Drunk).Plan.Hash, Gen(SecretSeed.Drunk).Plan.Hash);
    }

    [Fact]
    public void DrunkWorldsHaveBothEvils()
    {
        var plain = Gen(SecretSeed.None).Plan.Regions.Select(r => r.Biome).ToHashSet();
        Assert.False(plain.Contains("corruption") && plain.Contains("crimson"));
        var drunk = Gen(SecretSeed.Drunk).Plan.Regions.Select(r => r.Biome).ToHashSet();
        Assert.Contains("corruption", drunk);
        Assert.Contains("crimson", drunk);
    }

    [Fact]
    public void NotTheBeesGrowsTheJungleAndItsHives()
    {
        var plain = Gen(SecretSeed.None).Plan;
        var bees = Gen(SecretSeed.NotTheBees).Plan;
        var j0 = plain.Regions.First(r => r.Biome == "jungle");
        var j1 = bees.Regions.First(r => r.Biome == "jungle");
        Assert.True(j1.RadiusX > j0.RadiusX * 1.4f);
        Assert.Equal(4 * plain.Features.OfKind<ChamberFeature>("hive").Count(), bees.Features.OfKind<ChamberFeature>("hive").Count());
    }

    [Fact]
    public void ForTheWorthyPlaysOneStepHarder()
    {
        Assert.Equal(Difficulty.Expert, new WorldSettings { Secret = SecretSeed.ForTheWorthy }.EffectiveDifficulty);
        Assert.Equal(Difficulty.Master, new WorldSettings { Difficulty = Difficulty.Expert, Secret = SecretSeed.ForTheWorthy }.EffectiveDifficulty);
        using var normal = TestGame.Create();
        using var worthy = TestGame.Create(secret: SecretSeed.ForTheWorthy);
        Assert.Equal(2 * normal.World.Npcs.Scale, worthy.World.Npcs.Scale);
    }

    [Fact]
    public void CelebrationLetsThePartyStartWithoutThePartyGirl()
    {
        using var g = TestGame.Create(secret: SecretSeed.Celebration);
        var party = g.Content.Events.Get("party");
        var ctx = g.World.Session.ContextAt(g.Player.FeetCell);
        Assert.True(party.Conditions.All(c => Strata.Core.Conditions.ConditionCompiler.Compile(c).Evaluate(ctx)));
        using var plain = TestGame.Create();
        var ctx0 = plain.World.Session.ContextAt(plain.Player.FeetCell);
        Assert.False(party.Conditions.All(c => Strata.Core.Conditions.ConditionCompiler.Compile(c).Evaluate(ctx0)));
    }

    [Fact]
    public void NoTrapsFillsTheDungeonWithDartTraps()
    {
        static int Traps(SecretSeed secret)
        {
            var plan = Gen(secret).Plan;
            var dungeon = plan.Features.All.OfType<DungeonFeature>().Single();
            return dungeon.Blueprint.Ops.OfType<ObjectOp>().Count(o => o.ObjectId == "dart_trap");
        }
        int plain = Traps(SecretSeed.None), traps = Traps(SecretSeed.NoTraps);
        Assert.True(traps >= plain * 2 && traps > plain + 2, $"plain {plain}, no traps {traps}");
    }
}
