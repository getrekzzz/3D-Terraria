using System.Numerics;
using Strata.Core.Common;
using Strata.Core.Gameplay.Town;
using Strata.Core.Tests.Support;

namespace Strata.Core.Tests.Gameplay;

/// <summary>P5-09: hostile creatures go after town residents when no player is nearer.</summary>
public class TownDefenseTests
{
    private static TestGame Create()
    {
        var g = TestGame.Create();
        g.World.Spawner.Enabled = false;
        g.World.Town.ArrivalsEnabled = false;
        g.World.Session.Clock.SetHour(22f);
        return g;
    }

    private static Vector3 Feet(TestGame g, float x) => TownSystem.NodeFeet(g.Floor + Int3.Up) + new Vector3(x, 0, 0);

    private static TownNpc Guide(TestGame g, float x) =>
        g.World.Town.Summon(g.Content.TownNpcs.Get("guide"), g.World.Navigator.NearestStandable(g.Floor + Int3.Up + new Int3((int)(x / Units.CellSize), 0, 0), Strata.Core.Gameplay.Navigation.NavAgent.Humanoid, 2)!.Value);

    [Fact]
    public void ZombiesHuntResidentsWhenThePlayerIsFar()
    {
        using var g = Create();
        g.Player.Position = Feet(g, -5f);
        var guide = Guide(g, 4.5f);
        var zombie = g.World.Npcs.Spawn(g.Content.Enemies.Get("zombie"), Feet(g, 2.5f))!;
        g.World.Tick();
        Assert.Same(guide, zombie.ResidentTarget);
        for (int i = 0; i < 600 && guide.Life == guide.Def.Life; i++)
        {
            g.Player.Position = Feet(g, -5f);
            g.World.Tick();
        }
        Assert.True(guide.Life < guide.Def.Life);
    }

    [Fact]
    public void ThePlayerIsPreferredWhenClose()
    {
        using var g = Create();
        Guide(g, 3f);
        g.Player.Position = Feet(g, -1f);
        var zombie = g.World.Npcs.Spawn(g.Content.Enemies.Get("zombie"), Feet(g, 1f))!;
        g.World.Tick();
        Assert.Null(zombie.ResidentTarget);
    }

    [Fact]
    public void BossesAndBoundNpcsAreNoTargets()
    {
        using var g = Create();
        g.Player.Position = Feet(g, -5f);
        var guide = Guide(g, 4.5f);
        var eye = g.World.Npcs.Spawn(g.Content.Enemies.Get("eye_of_cthulhu"), Feet(g, 3.5f) + new Vector3(0, 3, 0))!;
        guide.Bound = true;
        var zombie = g.World.Npcs.Spawn(g.Content.Enemies.Get("zombie"), Feet(g, 3f))!;
        g.World.Tick();
        Assert.Null(eye.ResidentTarget);
        Assert.Null(zombie.ResidentTarget);
    }
}
