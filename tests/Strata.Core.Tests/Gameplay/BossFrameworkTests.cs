using System.Numerics;
using Strata.Core.Content;
using Strata.Core.Content.Defs;
using Strata.Core.Gameplay.Entities;
using Strata.Core.Gameplay.Messages;
using Strata.Core.Tests.Support;
using Strata.Core.World;

namespace Strata.Core.Tests.Gameplay;

/// <summary>The shared boss framework: parts that fall with their body, Expert context, conditional enrage.</summary>
public class BossFrameworkTests
{
    private static TestGame Arena(Difficulty difficulty = Difficulty.Classic)
    {
        var g = TestGame.Create(difficulty: difficulty);
        g.World.Spawner.Enabled = false;
        g.Player.State.BonusLife = 100_000;
        g.Player.State.Recompute();
        g.Player.State.Vitals.Life = g.Player.State.Vitals.MaxLife;
        return g;
    }

    private static Npc MoonLord(TestGame g) =>
        g.World.Npcs.Spawn(g.Content.Enemies.Get("moon_lord"), g.Player.Position + new Vector3(0, 6, 16))!;

    [Fact]
    public void KillingTheBodyEndsTheFightWhilePartsLive()
    {
        using var g = Arena();
        var core = MoonLord(g);
        var parts = g.World.Npcs.All.Where(n => n.Leader == core).ToList();
        Assert.Equal(3, parts.Count);
        Assert.All(parts, p => Assert.True(p.IsPart && p.InBossBar));
        var messages = new List<(string, MessageKind)>();
        g.World.Npcs.Messaged += (text, kind) => messages.Add((text, kind));
        g.World.Npcs.Kill(core);
        Assert.True(g.World.Progression.IsSet("defeated_moon_lord"));
        Assert.Contains(("Moon Lord has been defeated!", MessageKind.BossDefeated), messages);
        int drops = g.World.Drops.Items.Count;
        g.World.Tick();
        Assert.DoesNotContain(g.World.Npcs.All, n => parts.Contains(n));
        Assert.True(g.World.Drops.Items.Count <= drops, "parts dropped loot");
        Assert.DoesNotContain(g.World.Npcs.All, n => n.Def.Id == "true_eye_of_cthulhu"); // no death effects either
    }

    [Fact]
    public void PartsStayWithTheirBodyWhenThePlayerIsFar()
    {
        using var g = Arena();
        var core = MoonLord(g);
        Assert.Equal(220f, core.Def.Boss!.ArenaRadius);
        g.Player.Position += new Vector3(100f, 0, 0);
        for (int t = 0; t < 300; t++)
        {
            g.World.Tick();
            g.Player.State.Vitals.Life = g.Player.State.Vitals.MaxLife;
        }
        Assert.False(core.Removed);
        Assert.Equal(2, g.World.Npcs.All.Count(n => n.Def.Id == "moon_lord_hand" && n.Leader == core));
        Assert.Single(g.World.Npcs.All, n => n.Def.Id == "moon_lord_head");
    }

    [Fact]
    public void RespawnDoesNotStripABossOfItsParts()
    {
        using var g = Arena();
        var core = MoonLord(g);
        g.World.Respawn(g.Player);
        Assert.False(core.Removed);
        Assert.Equal(3, g.World.Npcs.All.Count(n => n.Leader == core && n.IsPart));
    }

    private static EnemyDef Raging(bool latch) => new()
    {
        Id = latch ? "test_latched_rage" : "test_rage",
        Life = 5000,
        Defense = 12,
        ContactDamage = false,
        Boss = new BossInfo
        {
            Title = "Test Rage",
            Enrage = new Enrage { When = new[] { TypedSpec.Of("time", new { Is = "day" }) }, SetDefense = 9999, SetDamage = 1000, Latch = latch, Message = "It burns!" },
        },
    };

    [Fact]
    public void EnrageConditionsApplyLethalStats()
    {
        using var g = Arena();
        var messages = new List<string>();
        g.World.Npcs.Messaged += (text, _) => messages.Add(text);
        g.World.Session.Clock.SetHour(12f);
        var npc = g.World.Npcs.Spawn(Raging(latch: false), g.Player.Position + new Vector3(0, 0.5f, 5))!;
        var latched = g.World.Npcs.Spawn(Raging(latch: true), g.Player.Position + new Vector3(0, 0.5f, -5))!;
        g.Ticks(31);
        Assert.True(npc.Frenzied);
        Assert.Equal(9999, npc.Defense);
        Assert.Equal(9999, latched.Defense);
        Assert.Equal(2, messages.Count(m => m == "It burns!"));

        g.World.Session.Clock.SetHour(22f);
        g.Ticks(31);
        Assert.False(npc.Frenzied);
        Assert.Equal(12, npc.Defense);
        Assert.Equal(9999, latched.Defense); // latched: the rage holds
        Assert.Equal(2, messages.Count(m => m == "It burns!"));
    }

    [Fact]
    public void ExpertContextFollowsDifficulty()
    {
        using (var journey = Arena(Difficulty.Journey)) Assert.False(journey.World.Npcs.Context.Expert);
        using (var classic = Arena(Difficulty.Classic)) Assert.False(classic.World.Npcs.Context.Expert);
        using (var master = Arena(Difficulty.Master)) Assert.True(master.World.Npcs.Context.Expert);
    }

    [Fact]
    public void BossesAnnounceTheirArrivalInBossPurple()
    {
        using var g = Arena();
        var messages = new List<(string, MessageKind)>();
        g.World.Npcs.Messaged += (text, kind) => messages.Add((text, kind));
        MoonLord(g);
        Assert.Equal(new[] { ("Moon Lord has awoken!", MessageKind.BossAwoken) }, messages); // its parts join the fight silently
        Assert.Equal("#af4bff", MessageColors.Hex(MessageKind.BossAwoken));
    }

    [Fact]
    public void BossProbesReadTheLivingBoss()
    {
        using var g = Arena();
        var core = MoonLord(g);
        var ctx = new Strata.Core.Commands.CommandContext(g.World, new Strata.Core.Commands.HeadlessCommandHost());
        Assert.Equal(core.Life, ctx.ReadProbe("boss.moon_lord.life"));
        Assert.Equal(3, ctx.ReadProbe("boss.moon_lord.parts"));
        Assert.Equal(0, ctx.ReadProbe("boss.king_slime.life"));
    }

    [Fact]
    public void SlayingEveryPieceDefeatsTheBossOnce()
    {
        using var g = Arena();
        var core = MoonLord(g);
        int defeats = 0, messages = 0;
        g.World.Npcs.BossDefeated += _ => defeats++;
        g.World.Npcs.Messaged += (_, kind) => messages += kind == MessageKind.BossDefeated ? 1 : 0;
        var pieces = g.World.Npcs.All.Where(n => n == core || n.Leader == core).ToList();
        foreach (var n in pieces) g.World.Npcs.Kill(n); // what `slay` does: the core first, then its parts
        Assert.Equal(1, defeats);
        Assert.Equal(1, messages);
        Assert.DoesNotContain(g.World.Npcs.All, n => n.Def.Id == "true_eye_of_cthulhu");
        Assert.DoesNotContain(g.World.Npcs.All, n => n.Leader == core);
    }

    [Fact]
    public void PartsAreDeadTheMomentTheirBodyFalls()
    {
        using var g = Arena();
        var core = MoonLord(g);
        var hand = g.World.Npcs.All.First(n => n.Def.Id == "moon_lord_hand");
        int defeats = 0;
        g.World.Npcs.BossDefeated += _ => defeats++;
        g.World.Npcs.Kill(core);
        Assert.False(hand.Alive);
        Assert.False(g.World.Npcs.Hit(hand, g.Player, 999_999, DamageClass.Melee, 0, 0f, g.Player.Center, melee: true));
        Assert.Equal(1, defeats);
    }

    [Fact]
    public void ReelingKeepsTheBrainsDamageScale()
    {
        using var g = Arena();
        var boss = g.World.Npcs.Spawn(g.Content.Enemies.Get("duke_fishron"), g.Player.Position + new Vector3(0, 6f, 4f))!;
        boss.Life = boss.MaxLife * 4 / 10; // its furious phase hits harder
        g.Ticks(2);
        float furious = boss.DamageScale;
        Assert.True(furious > 1f);
        boss.StunTicks = 5;
        g.Ticks(3);
        Assert.Equal(furious, boss.DamageScale);
    }
}
