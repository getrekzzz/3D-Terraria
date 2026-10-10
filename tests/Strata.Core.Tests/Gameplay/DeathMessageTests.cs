using System.Numerics;
using Strata.Core.Content.Loading;
using Strata.Core.Gameplay.Combat;
using Strata.Core.Presentation;
using Strata.Core.Voxels;
using Strata.Core.Tests.Support;

namespace Strata.Core.Tests.Gameplay;

/// <summary>Death messages name what killed the player: the creature, the fall, the lava, the debuff.</summary>
public class DeathMessageTests
{
    private static readonly DeathMessages Shipped = DeathMessages.Load(new FileSystemDataSource(TestPaths.ProjectRoot));

    private static TestGame Game(List<string> slain)
    {
        var g = TestGame.Create();
        g.World.Spawner.Enabled = false;
        g.World.Liquids.Enabled = false;
        g.World.Deaths = Shipped;
        g.World.PlayerSlain += (p, text) => slain.Add(text);
        return g;
    }

    private static void TickUntilSlain(TestGame g, List<string> slain, int max)
    {
        for (int t = 0; t < max && slain.Count == 0; t++) g.Ticks(1);
        Assert.Single(slain);
    }

    [Fact]
    public void ContactDeathNamesTheEnemy()
    {
        var slain = new List<string>();
        using var g = Game(slain);
        g.Player.State.Vitals.Life = 5;
        var zombie = g.World.Npcs.Spawn(g.Content.Enemies.Get("zombie"), g.Player.Position + new Vector3(0.6f, 0, 0))!;
        TickUntilSlain(g, slain, 600);
        Assert.Equal(new Harm(HarmKind.Enemy, "a Zombie", "zombie"), g.Player.LastHarm);
        Assert.Equal($"{g.Player.State.Name} was slain by a Zombie.", slain[0]);
        Assert.True(zombie.Alive);
    }

    [Fact]
    public void FallAndLavaMessages()
    {
        Assert.Equal("Wanderer fell to their death.", Shipped.Format("Wanderer", Harm.Fall, 0));
        Assert.Equal("Wanderer tried to swim in lava.", Shipped.Format("Wanderer", Harm.Lava, 0));
        Assert.Equal("Wanderer forgot to breathe.", Shipped.Format("Wanderer", Harm.Drowning, 0));

        var slain = new List<string>();
        using var g = Game(slain);
        g.Player.State.Vitals.Life = 1;
        var feet = g.Player.FeetCell;
        g.World.Editor.FillLiquid(feet, feet + Common.Int3.Up, g.Content.Liquids.Get("lava"), LiquidCode.MaxLevel);
        TickUntilSlain(g, slain, 120);
        Assert.Equal(HarmKind.Lava, g.Player.LastHarm.Kind);
        Assert.Equal($"{g.Player.State.Name} tried to swim in lava.", slain[0]);
    }

    [Fact]
    public void DebuffDeathNamesTheBuff()
    {
        var slain = new List<string>();
        using var g = Game(slain);
        g.Player.State.Buffs.Add(g.Content.Buffs.Get("poisoned"), 3600);
        g.Player.State.Buffs.Add(g.Content.Buffs.Get("on_fire"), 3600);
        g.Player.State.Recompute();
        Assert.Equal("on_fire", g.Player.State.Effects.DotSource?.Id); // the stronger of the two
        g.Player.State.Vitals.Life = 2;
        TickUntilSlain(g, slain, 180);
        Assert.Equal(new Harm(HarmKind.Debuff, "On Fire!", "on_fire"), g.Player.LastHarm);
        Assert.Equal($"{g.Player.State.Name} burned to death.", slain[0]);
        Assert.Equal("Wanderer was consumed by Venom.", Shipped.Format("Wanderer", new Harm(HarmKind.Debuff, "Venom!", "venom"), 0));
    }

    [Fact]
    public void EveryHarmKindHasAMessage()
    {
        Assert.Empty(Shipped.Problems());
        foreach (var kind in Enum.GetValues<HarmKind>())
            for (int v = 0; v < 8; v++)
            {
                string line = Shipped.Format("Wanderer", new Harm(kind, "a Zombie"), v);
                Assert.StartsWith("Wanderer", line);
                Assert.DoesNotContain("{", line);
            }
        var broken = new DeathMessages { Lines = new() { ["enemy"] = new[] { "{player} was slain by {source}." } } };
        Assert.Contains("no line for 'fall'", broken.Problems());
    }

    [Fact]
    public void VariantIsDeterministic()
    {
        var zombie = Harm.By(TestPaths.Content.Enemies.Get("zombie"));
        Assert.Equal(Shipped.Format("Wanderer", zombie, 3), Shipped.Format("Wanderer", zombie, 3));
        Assert.NotEqual(Shipped.Format("Wanderer", zombie, 0), Shipped.Format("Wanderer", zombie, 1));
        Assert.Equal(Shipped.Format("Wanderer", zombie, 0), Shipped.Format("Wanderer", zombie, Shipped.Lines["enemy"].Length));
        Assert.Equal("King Slime", Harm.By(TestPaths.Content.Enemies.Get("king_slime")).Source); // bosses by name
        Assert.Equal("an Antlion", Harm.WithArticle("Antlion"));
        Assert.Equal("a Unicorn", Harm.WithArticle("Unicorn"));
    }

    [Fact]
    public void ShotsNameTheirShooter()
    {
        var slain = new List<string>();
        using var g = Game(slain);
        g.Player.State.Vitals.Life = 1;
        var archer = g.Content.Enemies.Get("demon");
        Assert.NotNull(archer.Ranged);
        var arrow = g.Content.Projectiles.Get(archer.Ranged!.Projectile);
        var shot = g.World.Projectiles.Spawn(arrow, g.Player.Center + new Vector3(0, 0, 1.5f), new Vector3(0, 0, -20f), null, 30, 2f,
            Strata.Core.Content.Defs.DamageClass.None, 0);
        shot.Shooter = archer;
        TickUntilSlain(g, slain, 60);
        Assert.Equal(HarmKind.Projectile, g.Player.LastHarm.Kind);
        Assert.Contains("a Demon", slain[0]);
    }
}
