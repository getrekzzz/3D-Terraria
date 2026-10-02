using System.Numerics;
using Strata.Core.Content.Defs;
using Strata.Core.Content.Specs;
using Strata.Core.Gameplay.Entities;
using Strata.Core.Tests.Support;

namespace Strata.Core.Tests.Gameplay;

/// <summary>P4-24: The Tongued, Leeches, faster lasers as it weakens, one life for mouth and eyes.</summary>
public partial class MawwallTests
{
    private static (TestGame g, Npc wall, MawwallParams p) Risen()
    {
        var g = Create();
        var at = IntoUnderworld(g);
        var def = g.Content.Enemies.Get("wall_of_flesh");
        var wall = g.World.Npcs.Spawn(def, at)!;
        g.World.Tick();
        return (g, wall, def.Brain.Parse<MawwallParams>());
    }

    private static void InFront(TestGame g, Npc wall, float distance)
    {
        g.Player.Position = new Vector3(wall.Center.X + wall.Direction.X * distance, wall.Center.Y - 1f, wall.Center.Z);
        g.Player.State.Vitals.Life = g.Player.State.Vitals.MaxLife;
    }

    [Fact]
    public void FleeingPlayersAreTongued()
    {
        var (g, wall, p) = Risen();
        using var scope = g;
        InFront(g, wall, 20f);
        g.World.Tick();
        Assert.False(g.Player.State.Buffs.Has(p.TongueBuff));
        Assert.Equal(Vector3.Zero, g.Player.ExternalVelocity);

        InFront(g, wall, p.TongueDistance + 10f);
        g.World.Tick();
        Assert.True(g.Player.State.Buffs.Has(p.TongueBuff));
        Assert.True(g.Player.State.Effects.NoItemUse);
        var pull = g.Player.ExternalVelocity;
        Assert.Equal(p.TonguePull, pull.Length(), 2);
        Assert.True(Vector3.Dot(pull, wall.Center - g.Player.Center) > 0, "reeled towards the mouth");

        // Still held inside the tongue distance, all the way into the mouth; then it lets go.
        bool swallowed = false;
        for (int i = 0; i < 600 && !swallowed; i++)
        {
            g.World.Tick();
            g.Player.State.Vitals.Life = g.Player.State.Vitals.MaxLife;
            swallowed = !g.Player.State.Buffs.Has(p.TongueBuff);
        }
        Assert.True(swallowed);
        Assert.True(Vector3.Distance(g.Player.Center, wall.Center) < wall.HalfExtents.X + 2f);
        g.World.Tick();
        Assert.Equal(Vector3.Zero, g.Player.ExternalVelocity);
    }

    private static int Leeches(float life, int ticks)
    {
        var (g, wall, _) = Risen();
        using var scope = g;
        wall.Life = (int)(wall.MaxLife * life);
        int born = 0;
        g.World.Npcs.Spawned += n => { if (n.Def.Id == "leech") born++; };
        for (int i = 0; i < ticks; i++)
        {
            InFront(g, wall, 12f);
            g.World.Tick();
            foreach (var n in g.World.Npcs.All.Where(n => n.Def.Id == "leech" && n.Alive).ToList()) g.World.Npcs.Kill(n);
        }
        return born;
    }

    [Fact]
    public void LeechesCrawlOutMoreOftenAsItWeakens()
    {
        int healthy = Leeches(1f, 1300), hurt = Leeches(0.1f, 1300);
        Assert.Equal(3, healthy); // every 420 ticks at full life
        Assert.True(hurt >= 6, $"{hurt} leeches when weak"); // every ~193 ticks at a tenth
    }

    [Fact]
    public void LeechesStayUnderTheCap()
    {
        var (g, wall, p) = Risen();
        using var scope = g;
        wall.Life = wall.MaxLife / 10;
        for (int i = 0; i < 2000; i++)
        {
            InFront(g, wall, 12f);
            g.World.Tick();
        }
        Assert.InRange(g.World.Npcs.All.Count(n => n.Def.Id == "leech" && n.Alive), 1, p.MaxLeeches);
    }

    private static int Lasers(float life)
    {
        var (g, wall, _) = Risen();
        using var scope = g;
        wall.Life = (int)(wall.MaxLife * life);
        var shots = new HashSet<Projectile>();
        for (int i = 0; i < 600; i++)
        {
            InFront(g, wall, 12f);
            g.World.Tick();
            foreach (var s in g.World.Projectiles.All)
                if (s.Def.Id == "wall_of_flesh_laser") shots.Add(s);
        }
        return shots.Count;
    }

    [Fact]
    public void EyesFireFasterWhenHurt()
    {
        int healthy = Lasers(1f), hurt = Lasers(0.1f);
        Assert.InRange(healthy, 6, 10); // every 70 ticks
        Assert.True(hurt * 10 >= healthy * 17, $"{hurt} vs {healthy}"); // every ~30 ticks near death
    }

    [Fact]
    public void LifeIsSharedByMouthAndEyes()
    {
        var (g, wall, _) = Risen();
        using var scope = g;
        Assert.Equal((int)(8000 * g.World.Npcs.Scale), wall.MaxLife);
        int before = wall.Life;
        g.Player.Position = wall.Center + wall.Direction * 30f;
        g.World.Projectiles.Spawn(g.Content.Projectiles.Get("wooden_arrow"), wall.Segments[0], Vector3.Zero, g.Player, 200, 0, DamageClass.Ranged, 0);
        g.World.Tick();
        Assert.True(wall.Life < before, "an arrow in the eye hurts the wall");
        Assert.DoesNotContain(g.World.Npcs.All, n => n.Def.Id == "wall_of_flesh" && n != wall);
    }
}
