using Strata.Core.Content.Loading;
using Strata.Core.Gameplay.Events;
using Strata.Core.Tests.Support;

namespace Strata.Core.Tests.Gameplay;

/// <summary>Fallen Stars (P3-30): they fall near players at night, land as items, and fade at dawn.</summary>
public class StarfallTests
{
    private static TestGame Night(float chance = 1f)
    {
        var g = TestGame.Create();
        g.World.Spawner.Enabled = false;
        g.World.Starfall.Config = new StarfallConfig { ChancePerSecond = chance };
        g.World.Session.Clock.SetHour(0f);
        return g;
    }

    private static int Stars(TestGame g) => g.World.Drops.Items.Where(d => d.Stack.Item?.Id == "fallen_star").Sum(d => d.Stack.Count);

    [Fact]
    public void StarsFallAtNightAndLandAsItems()
    {
        using var g = Night();
        int landed = 0;
        g.World.Projectiles.Landed += _ => landed++;
        for (int t = 0; t < 600 && Stars(g) == 0; t++) g.World.Tick();
        Assert.True(g.World.Starfall.Fallen > 0, "no star started");
        Assert.True(Stars(g) >= 1, "no Fallen Star landed");
        Assert.True(landed >= 1);
        Assert.True(g.World.Projectiles.All.Count(p => p.Def.Id == "falling_star") <= g.World.Starfall.Config.MaxActive);
    }

    [Fact]
    public void NoStarsByDay()
    {
        using var g = Night();
        g.World.Session.Clock.SetHour(12f);
        g.Ticks(600);
        Assert.Equal(0, g.World.Starfall.Fallen);
        Assert.Equal(0, Stars(g));
    }

    [Fact]
    public void FallenStarsFadeAtDawnButOtherDropsStay()
    {
        using var g = Night(0f);
        var star = g.World.Starfall.FallNear(g.Player);
        Assert.NotNull(star);
        for (int t = 0; t < 600 && Stars(g) == 0; t++) g.World.Tick();
        Assert.Equal(1, Stars(g));
        g.World.Drops.Spawn(new Strata.Core.Gameplay.Items.ItemStack(g.Content.Items.Get("gel"), 3), g.Player.Position + new System.Numerics.Vector3(2, 0.5f, 0), default);
        g.World.Session.Clock.SetHour(Strata.Core.Simulation.GameClock.DawnHour + 0.01f);
        g.World.Tick();
        Assert.Equal(0, Stars(g));
        Assert.Equal(1, g.World.Starfall.Faded);
        Assert.Contains(g.World.Drops.Items, d => d.Stack.Item?.Id == "gel");
    }

    [Fact]
    public void ShippedConfigValidates()
    {
        var config = StarfallConfig.Load(new FileSystemDataSource(TestPaths.ProjectRoot));
        Assert.Empty(config.Validate());
        Assert.True(TestPaths.Content.Projectiles.Contains(config.Projectile));
        Assert.True(TestPaths.Content.Items.Get("fallen_star").FadesAtDawn);
        Assert.NotEmpty(new StarfallConfig { Speed = 0, Height = 5 }.Validate());
    }
}
