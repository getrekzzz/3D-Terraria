using System.Numerics;
using Strata.Core.Common;
using Strata.Core.Gameplay.Town;
using Strata.Core.Tests.Support;
using Strata.Core.Voxels.TileObjects;

namespace Strata.Core.Tests.Gameplay;

/// <summary>P5-11: residents sit in their room's chair at night, sleep in a bed late, and get up in the morning.</summary>
public class SeatingTests
{
    private static (TestGame G, TownNpc Guide) Housed(bool bed = false)
    {
        var g = TestGame.Create();
        g.World.Spawner.Enabled = false;
        g.World.Town.ArrivalsEnabled = false;
        var floor = g.Floor + new Int3(7, 0, 0);
        TestHouses.Build(g, floor);
        if (bed) Assert.True(g.World.Session.World.TileObjects.TryAdd(new TileObjectInstance(g.Content.TileObjects.Get("bed"), floor + new Int3(2, 1, -4), 0, 0)));
        var guide = g.World.Town.Find("guide") ?? g.World.Town.Summon(g.Content.TownNpcs.Get("guide"), g.World.Navigator.NearestStandable(g.Floor + new Int3(2, 1, 0), Strata.Core.Gameplay.Navigation.NavAgent.Humanoid, 3)!.Value);
        g.World.Session.Clock.SetHour(12f);
        g.World.Town.CheckArrivals(force: true);
        Assert.NotNull(guide.Home);
        return (g, guide);
    }

    private static void Run(TestGame g, int ticks, Func<bool> until)
    {
        for (int i = 0; i < ticks && !until(); i++) g.World.Tick();
    }

    private static TileObjectInstance SeatOf(TestGame g, TownNpc r) => g.World.Session.World.TileObjects.Get(r.Seat!.Value)!;

    [Fact]
    public void SitsInChairAtNight()
    {
        var (g, guide) = Housed();
        using var _ = g;
        g.World.Session.Clock.SetHour(21f);
        Run(g, 1500, () => guide.Posture == ResidentPosture.Sit);
        Assert.Equal(ResidentPosture.Sit, guide.Posture);
        var chair = SeatOf(g, guide);
        Assert.Equal("wooden_chair", chair.Def.Id);
        Assert.True(Vector3.Distance(guide.Position, TownSystem.SeatPoint(chair)) < 0.3f);
        g.World.Tick();
        Assert.True(Vector3.Distance(guide.Position, TownSystem.SeatPoint(chair)) < 0.3f); // stays put
    }

    [Fact]
    public void StandsUpInTheMorning()
    {
        var (g, guide) = Housed();
        using var _ = g;
        g.World.Session.Clock.SetHour(21f);
        Run(g, 1500, () => guide.Posture == ResidentPosture.Sit);
        Assert.Equal(ResidentPosture.Sit, guide.Posture);
        g.World.Session.Clock.SetHour(6f);
        g.World.Tick();
        Assert.Equal(ResidentPosture.Stand, guide.Posture);
        Assert.Null(guide.Seat);
    }

    [Fact]
    public void StandsWhenChairRemoved()
    {
        var (g, guide) = Housed();
        using var _ = g;
        g.World.Session.Clock.SetHour(21f);
        Run(g, 1500, () => guide.Posture == ResidentPosture.Sit);
        var chair = SeatOf(g, guide);
        g.World.Session.World.TileObjects.Remove(chair);
        g.World.Tick();
        Assert.Equal(ResidentPosture.Stand, guide.Posture);
        Assert.Null(guide.Seat);
    }

    [Fact]
    public void SleepsInBedAfter22()
    {
        var (g, guide) = Housed(bed: true);
        using var _ = g;
        g.World.Session.Clock.SetHour(22.5f);
        Run(g, 2000, () => guide.Posture == ResidentPosture.Sleep);
        Assert.Equal(ResidentPosture.Sleep, guide.Posture);
        Assert.Equal("bed", SeatOf(g, guide).Def.Id);
    }

    [Fact]
    public void GetsUpToTalk()
    {
        var (g, guide) = Housed();
        using var _ = g;
        g.World.Session.Clock.SetHour(21f);
        Run(g, 1500, () => guide.Posture == ResidentPosture.Sit);
        g.Player.Position = guide.Position + new Vector3(1f, 0, 0);
        Assert.True(g.World.Town.BeginTalk(guide, g.Player));
        Assert.Equal(ResidentPosture.Stand, guide.Posture);
    }
}
