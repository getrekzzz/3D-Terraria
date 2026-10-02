using System.Numerics;
using Strata.Core.Content.Loading;
using Strata.Core.Gameplay.Entities;
using Strata.Core.Presentation;
using Strata.Core.Tests.Support;

namespace Strata.Core.Tests.Gameplay;

public class CreatureMaterialTests
{
    private static readonly CreatureMaterialsConfig Config = CreatureMaterialsConfig.Load(new FileSystemDataSource(TestPaths.ProjectRoot));

    [Fact]
    public void ShippedConfigResolvesEveryEnemy()
    {
        Assert.Empty(Config.Validate(TestPaths.Content));
        foreach (var e in TestPaths.Content.Enemies.All) Assert.True(Config.Materials.ContainsKey(Config.ResolveId(e)), e.Id);
        var c = TestPaths.Content.Enemies;
        Assert.Equal("gel", Config.ResolveId(c.Get("green_slime")));
        Assert.Equal("bone", Config.ResolveId(c.Get("skeleton")));
        Assert.Equal("flesh", Config.ResolveId(c.Get("zombie")));
    }

    [Fact]
    public void AnUnknownMaterialIsAnError()
    {
        var bad = new CreatureMaterialsConfig
        {
            Materials = { ["flesh"] = new CreatureMaterial(), ["odd"] = new CreatureMaterial { Dust = new MaterialDust { Recipe = "Glitter" } } },
            ByModel = { ["zombie"] = "jelly" },
        };
        var problems = bad.Validate(TestPaths.Content);
        Assert.Contains(problems, p => p.Contains("Glitter"));
        Assert.Contains(problems, p => p.Contains("jelly"));
    }

    [Fact]
    public void HitsAndDamageOverTimeCarryTheirCreatureAndKind()
    {
        using var g = TestGame.Create();
        g.World.Spawner.Enabled = false;
        var events = new List<DamageEvent>();
        g.World.Npcs.Damaged += events.Add;
        var zombie = g.World.Npcs.Spawn(g.Content.Enemies.Get("zombie"), g.Player.Position + new Vector3(3, 0, 0))!;
        g.World.Npcs.Hit(zombie, g.Player, 5, Strata.Core.Content.Defs.DamageClass.Melee, 0, 0f, g.Player.Position, melee: true);
        var hit = Assert.Single(events);
        Assert.Equal(zombie.Id, hit.NpcId);
        Assert.Equal(DamageKind.Hit, hit.Kind);
        Assert.Same(zombie, g.World.Npcs.Find(hit.NpcId));
        events.Clear();
        g.World.Npcs.AddBuff(zombie, g.Content.Buffs.Get("on_fire"), 600);
        g.Ticks(130);
        Assert.Contains(events, e => e.Kind == DamageKind.Periodic && e.NpcId == zombie.Id);
    }
}
