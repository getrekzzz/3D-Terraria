using System.Numerics;
using Strata.Core.Conditions;
using Strata.Core.Content.Defs;
using Strata.Core.Gameplay.Crafting;
using Strata.Core.Gameplay.Entities;
using Strata.Core.Gameplay.Items;
using Strata.Core.Tests.Support;

namespace Strata.Core.Tests.Gameplay;

public class LunarTests
{
    private static readonly string[] Pillars = { "solar_pillar", "vortex_pillar", "nebula_pillar", "stardust_pillar" };

    private static TestGame Arena()
    {
        var g = TestGame.Create();
        g.World.Spawner.Enabled = false;
        return g;
    }

    private static bool Hit(TestGame g, Npc npc) =>
        g.World.Npcs.Hit(npc, g.Player, 50, DamageClass.Melee, 0, 0f, g.Player.Center, melee: true);

    [Fact]
    public void DevoteesGatherAtTheDungeonAfterGolemAndDisturbingThemCallsTheCultist()
    {
        using var g = Arena();
        var cultist = g.Content.Enemies.Get("lunatic_cultist");
        var ritual = cultist.Boss!.Ritual!;
        var site = TestGame.CellFeet(g.World.Session.Generator.Plan.Landmarks[ritual.Landmark]);
        g.Player.Position = site;
        Assert.Null(g.World.Rituals.Ready(cultist, ritual)); // Golem still stands
        g.World.Progression.Set("defeated_golem");
        Assert.Equal(site, g.World.Rituals.Ready(cultist, ritual));

        // Gather them in the arena, where the chunks are loaded.
        var here = TestGame.CellFeet(g.Floor + new Common.Int3(0, 1, 0));
        g.Player.Position = here + new Vector3(0, 0, 8);
        g.World.Rituals.Gather(ritual, here);
        Assert.Equal(4, g.World.Npcs.All.Count(n => n.Def.Id == "lunatic_devotee"));
        Assert.Equal(2, g.World.Npcs.All.Count(n => n.Def.Id == "cultist_archer"));
        var tablet = Assert.Single(g.World.Npcs.All, n => n.Def.Id == "mysterious_tablet");
        Assert.False(Hit(g, tablet));

        Assert.True(Hit(g, g.World.Npcs.All.First(n => n.Def.Id == "lunatic_devotee")));
        Assert.Single(g.World.Npcs.All, n => n.Def == cultist);
        Assert.DoesNotContain(g.World.Npcs.All, n => n.Def.Id is "lunatic_devotee" or "mysterious_tablet");
        Assert.Equal(2, g.World.Npcs.All.Count(n => n.Def.Id == "cultist_archer")); // the guards stay to fight
    }

    [Fact]
    public void TheCultistsFallRaisesFourShieldedPillars()
    {
        using var g = Arena();
        g.World.Progression.Set("defeated_lunatic_cultist");
        Assert.True(g.World.Events.IsActive("lunar_events"));
        foreach (var id in Pillars) Assert.Single(g.World.Npcs.All, n => n.Def.Id == id);

        var solar = g.World.Npcs.All.First(n => n.Def.Id == "solar_pillar");
        g.World.Tick();
        Assert.True(g.World.Npcs.ShieldUp(solar));
        Assert.False(Hit(g, solar));
        // Its legion's deaths wear the shield down; other pillars' creatures do not count.
        var selenian = g.Content.Enemies.Get("selenian");
        var vortexian = g.Content.Enemies.Get("vortexian");
        var at = TestGame.CellFeet(g.Floor + new Common.Int3(3, 1, 3));
        g.World.Npcs.Kill(g.World.Npcs.Spawn(vortexian, at)!);
        Assert.Equal(0, g.World.Npcs.ShieldKills(solar.Def));
        for (int i = 0; i < 100; i++) g.World.Npcs.Kill(g.World.Npcs.Spawn(selenian, at)!);
        Assert.Equal(100, g.World.Npcs.ShieldKills(solar.Def));
        g.World.Tick();
        Assert.False(g.World.Npcs.ShieldUp(solar));
        Assert.True(Hit(g, solar));
    }

    [Fact]
    public void APillarAlreadyDestroyedDoesNotReturn()
    {
        using var g = Arena();
        g.World.Progression.Set("defeated_solar_pillar");
        g.World.Progression.Set("defeated_lunatic_cultist");
        Assert.DoesNotContain(g.World.Npcs.All, n => n.Def.Id == "solar_pillar");
        Assert.Equal(3, g.World.Npcs.All.Count(n => Pillars.Contains(n.Def.Id)));
    }

    [Fact]
    public void BreakingEveryPillarBringsTheMoonLord()
    {
        using var g = Arena();
        g.World.Progression.Set("defeated_lunatic_cultist");
        foreach (var pillar in g.World.Npcs.All.Where(n => Pillars.Contains(n.Def.Id)).ToList()) g.World.Npcs.Kill(pillar);
        foreach (var id in Pillars) Assert.True(g.World.Progression.IsSet("defeated_" + id));
        Assert.True(g.World.Progression.IsSet("defeated_pillars"));
        Assert.False(g.World.Events.IsActive("lunar_events"));
        Assert.Single(g.World.Npcs.All, n => n.Def.Id == "moon_lord");
    }

    [Fact]
    public void TheMoonLordsCoreIsSealedUntilHisHeadAndHandsFall()
    {
        using var g = Arena();
        g.Player.State.Vitals.Life = g.Player.State.Vitals.MaxLife = 50000;
        var core = g.World.Npcs.Spawn(g.Content.Enemies.Get("moon_lord"), g.Player.Position + new Vector3(0, 6, 16))!;
        var parts = g.World.Npcs.All.Where(n => n.Leader == core).ToList();
        Assert.Equal(new[] { "moon_lord_hand", "moon_lord_hand", "moon_lord_head" }, parts.Select(p => p.Def.Id).OrderBy(x => x));
        for (int t = 0; t < 60; t++)
        {
            g.World.Tick();
            g.Player.State.Vitals.Life = 50000;
        }
        Assert.True(core.Invulnerable);
        // The parts keep to their places on the body.
        foreach (var part in parts) Assert.True(Vector3.Distance(part.Center, part.Anchor) < 1f, $"{part.Def.Id} drifted {Vector3.Distance(part.Center, part.Anchor):0.0} m");
        Assert.Contains(g.World.Projectiles.All, p => p.Def.Id is "phantasmal_eye" or "phantasmal_bolt" or "phantasmal_sphere");

        foreach (var part in parts) g.World.Npcs.Kill(part);
        Assert.Equal(3, g.World.Npcs.All.Count(n => n.Def.Id == "true_eye_of_cthulhu"));
        Assert.False(g.World.Progression.IsSet("defeated_moon_lord")); // the core still lives
        g.World.Tick();
        Assert.False(core.Invulnerable);
        g.World.Npcs.Kill(core);
        Assert.True(g.World.Progression.IsSet("defeated_moon_lord"));
        g.Ticks(40);
        Assert.DoesNotContain(g.World.Npcs.All, n => n.Def.Id == "true_eye_of_cthulhu");
    }

    [Fact]
    public void TheAncientManipulatorForgesLuminiteAndTheSigil()
    {
        using var g = Arena();
        var book = new RecipeBook(g.Content);
        var ctx = new ConditionContext();
        var inv = new Inventory(8);
        inv.Add(new ItemStack(g.Content.Items.Get("luminite"), 8));
        var bar = book.RecipesFor(g.Content.Items.Get("luminite_bar"))[0];
        Assert.False(book.Craft(bar, inv, new HashSet<string> { "hardmode_anvil" }, ctx));
        Assert.True(book.Craft(bar, inv, new HashSet<string> { "ancient_manipulator" }, ctx));
        Assert.Equal(1, inv.Count(g.Content.Items.Get("luminite_bar")));

        foreach (var f in new[] { "solar", "vortex", "nebula", "stardust" }) inv.Add(new ItemStack(g.Content.Items.Get(f + "_fragment"), 12));
        var sigil = book.RecipesFor(g.Content.Items.Get("celestial_sigil"))[0];
        Assert.True(book.Craft(sigil, inv, new HashSet<string> { "ancient_manipulator" }, ctx));
        Assert.Equal(1, inv.Count(g.Content.Items.Get("celestial_sigil")));
    }

    [Fact]
    public void SentriesHoldTheirGroundAndFire()
    {
        using var g = Arena();
        var def = g.Content.Projectiles.Get("lunar_portal");
        var spot = g.Player.Center + new Vector3(0, 2, 0);
        var portal = g.World.Projectiles.Spawn(def, spot, new Vector3(1, 0, 0), g.Player, 50, 0, DamageClass.Summon, 0);
        g.World.Npcs.Spawn(g.Content.Enemies.Get("green_slime"), g.Player.Position + new Vector3(5, 0, 5));
        g.Player.Position += new Vector3(4, 0, 0); // the owner walks off
        bool fired = false;
        for (int t = 0; t < 50; t++)
        {
            g.World.Tick();
            fired |= g.World.Projectiles.All.Any(p => p.Def.Id == "lunar_portal_laser");
        }
        Assert.True(Vector3.Distance(portal.Position, spot) < 0.5f, $"moved {Vector3.Distance(portal.Position, spot):0.00} m");
        Assert.True(fired, "never fired");
    }
}
