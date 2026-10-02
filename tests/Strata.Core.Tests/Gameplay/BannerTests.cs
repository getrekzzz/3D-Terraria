using System.Numerics;
using Strata.Core.Common;
using Strata.Core.Gameplay.Banners;
using Strata.Core.Tests.Support;
using Strata.Core.Voxels.TileObjects;

namespace Strata.Core.Tests.Gameplay;

/// <summary>Enemy banners (X-03): one per coin-worthy enemy, dropped every 50 kills, empowering players near them.</summary>
public class BannerTests
{
    [Fact]
    public void EveryCoinWorthyEnemyHasABanner()
    {
        using var g = TestGame.Create();
        var missing = g.Content.Enemies.All
            .Where(e => e.Boss == null && !e.HasTag("critter") && !e.HasTag("boss") && e.Value > 0 && g.World.Banners.BannerFor(e) == null)
            .Select(e => e.Id).ToList();
        Assert.Empty(missing);
        var slime = g.World.Banners.BannerFor(g.Content.Enemies.Get("blue_slime"))!;
        Assert.Same(slime, g.World.Banners.BannerFor(g.Content.Enemies.Get("green_slime")));
        Assert.True(g.Content.Items.Contains(slime.Id));
    }

    [Fact]
    public void EveryFiftiethKillDropsTheBanner()
    {
        using var g = TestGame.Create();
        g.World.Spawner.Enabled = false;
        var zombie = g.Content.Enemies.Get("zombie");
        var banner = g.World.Banners.BannerFor(zombie)!;
        int Banners() => g.World.Drops.Items.Count(d => d.Stack.Item!.Id == banner.Id);
        for (int i = 0; i < 49; i++) Kill(g, "zombie");
        Assert.Equal(49, g.World.Banners.Kills(banner));
        Assert.Equal(0, Banners());
        Kill(g, "bald_zombie"); // variants count towards their banner
        Assert.Equal(1, Banners());
        var statue = g.World.Npcs.Spawn(zombie, g.Player.Position + new Vector3(6, 1, 0))!;
        statue.FromStatue = true;
        g.World.Npcs.Hit(statue, g.Player, 100000, Strata.Core.Content.Defs.DamageClass.Melee, 0, 0f, g.Player.Center, melee: false);
        Assert.Equal(50, g.World.Banners.Kills(banner)); // statue creatures do not count
    }

    private static void Kill(TestGame g, string enemy)
    {
        var npc = g.World.Npcs.Spawn(g.Content.Enemies.Get(enemy), g.Player.Position + new Vector3(6, 1, 0))!;
        g.World.Npcs.Hit(npc, g.Player, 100000, Strata.Core.Content.Defs.DamageClass.Melee, 0, 0f, g.Player.Center, melee: false);
        Assert.False(npc.Alive);
    }

    [Fact]
    public void ANearbyBannerEmpowersAgainstItsCreatures()
    {
        using var g = TestGame.Create();
        g.World.Spawner.Enabled = false;
        var zombie = g.World.Npcs.Spawn(g.Content.Enemies.Get("zombie"), g.Player.Position + new Vector3(8, 1, 0))!;
        var slime = g.World.Npcs.Spawn(g.Content.Enemies.Get("blue_slime"), g.Player.Position + new Vector3(-8, 1, 0))!;
        var banners = g.World.Banners;
        Assert.Equal(100, banners.Dealt(g.Player, zombie, 100));
        var def = banners.BannerFor(zombie.Def)!;
        Assert.True(g.World.Session.World.TileObjects.TryAdd(new TileObjectInstance(def, g.Player.FeetCell + new Int3(6, 2, 6), 0, 0)));
        g.Ticks(banners.Config.RefreshTicks + 1);
        Assert.Contains("zombie", banners.Near(g.Player));
        Assert.Equal(150, banners.Dealt(g.Player, zombie, 100));
        Assert.Equal(90, banners.Taken(g.Player, zombie, 100));
        Assert.Equal(100, banners.Dealt(g.Player, slime, 100)); // only its own creatures
        Assert.Equal(100, banners.Dealt(null, zombie, 100)); // traps and other sources gain nothing
        Assert.True(g.Player.State.Buffs.Has(banners.Config.Buff));
    }
}
