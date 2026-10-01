using System.Numerics;
using Strata.Core.Common;
using Strata.Core.Gameplay.Entities;
using Strata.Core.Gameplay.Interaction;
using Strata.Core.Tests.Support;

namespace Strata.Core.Tests.Gameplay;

public class EntityPickingTests
{
    private static TestGame Arena()
    {
        var g = TestGame.Create();
        g.World.Spawner.Enabled = false;
        g.World.Town.ArrivalsEnabled = false;
        return g;
    }

    /// <summary>Where the rays start: 2.5 m behind the player's feet, at slime height, looking along +Z.</summary>
    private static Vector3 Origin(TestGame g) => TestGame.CellFeet(g.Floor + Int3.Up) + new Vector3(0, 0.25f, -2.5f);

    private static Npc Spawn(TestGame g, string id, Vector3 feet) => g.World.Npcs.Spawn(g.Content.Enemies.Get(id), feet)!;

    [Fact]
    public void PicksCreatureInFront()
    {
        using var g = Arena();
        var origin = Origin(g);
        var slime = Spawn(g, "green_slime", origin + new Vector3(0, -0.25f, 4f));
        var pick = EntityPicking.Pick(g.World, origin, Vector3.UnitZ, 0f, 60f);
        Assert.NotNull(pick);
        Assert.Same(slime, pick!.Value.Npc);
        Assert.Null(pick.Value.Resident);
        Assert.Equal(4f - slime.HalfExtents.Z - EntityPicking.DefaultPad, pick.Value.Distance, 0.01f);
        Assert.Equal(("Green Slime", slime.Life, slime.MaxLife), EntityPicking.Describe(pick.Value));
        // Off to the side or pointing away: nothing.
        Assert.Null(EntityPicking.Pick(g.World, origin + new Vector3(2f, 0, 0), Vector3.UnitZ, 0f, 60f));
        Assert.Null(EntityPicking.Pick(g.World, origin, -Vector3.UnitZ, 0f, 60f));
    }

    [Fact]
    public void TerrainOccludes()
    {
        using var g = Arena();
        var origin = Origin(g);
        Spawn(g, "green_slime", origin + new Vector3(0, -0.25f, 4f));
        // A stone wall 2 m ahead, between the eye and the slime: the terrain hit caps the pick.
        var w = (origin + new Vector3(0, 0, 2f)) / Units.CellSize;
        var wall = new Int3((int)MathF.Floor(w.X), 0, (int)MathF.Floor(w.Z));
        for (int x = -2; x <= 2; x++)
            for (int y = -1; y <= 3; y++)
                g.World.Editor.SetBlock(new Int3(wall.X + x, g.Floor.Y + y, wall.Z), g.Content.BlockId("stone"));
        var hit = Targeting.Cast(g.World.Session.World, origin, Vector3.UnitZ, 60f, TargetFilter.None);
        Assert.NotNull(hit);
        Assert.Null(EntityPicking.Pick(g.World, origin, Vector3.UnitZ, 0f, hit!.Distance));
    }

    [Fact]
    public void WormSegmentResolvesToHead()
    {
        using var g = Arena();
        var origin = Origin(g);
        var worm = Spawn(g, "eater_of_worlds", origin + new Vector3(0, 6f, 20f));
        // Only segment 3 lies on the ray (the rest of the worm is far away).
        worm.Segments[3] = origin + new Vector3(0, 0, 5f);
        var pick = EntityPicking.Pick(g.World, origin, Vector3.UnitZ, 0f, 60f);
        Assert.NotNull(pick);
        Assert.Same(worm, pick!.Value.Npc);
        Assert.Equal(5f - worm.Def.Boss!.SegmentRadius - EntityPicking.DefaultPad, pick.Value.Distance, 0.01f);
        Assert.Equal(("Eater of Worlds", worm.Life, worm.MaxLife), EntityPicking.Describe(pick.Value));
    }

    [Fact]
    public void BehindMinTIsIgnored()
    {
        using var g = Arena();
        var origin = Origin(g);
        Spawn(g, "green_slime", origin + new Vector3(0, -0.25f, 1f));
        Assert.Null(EntityPicking.Pick(g.World, origin, Vector3.UnitZ, 2.5f, 60f));
        // A body the minT plane cuts through still counts, at minT.
        var pick = EntityPicking.Pick(g.World, origin, Vector3.UnitZ, 1f, 60f);
        Assert.Equal(1f, pick!.Value.Distance, 0.001f);
    }

    [Fact]
    public void NearestWins()
    {
        using var g = Arena();
        var origin = Origin(g);
        var far = Spawn(g, "green_slime", origin + new Vector3(0, -0.25f, 5f));
        var near = Spawn(g, "blue_slime", origin + new Vector3(0, -0.25f, 3f));
        var pick = EntityPicking.Pick(g.World, origin, Vector3.UnitZ, 0f, 60f);
        Assert.Same(near, pick!.Value.Npc);
        Assert.Equal(("Blue Slime", 25, 25), EntityPicking.Describe(pick.Value));
        g.World.Npcs.Kill(near);
        Assert.Same(far, EntityPicking.Pick(g.World, origin, Vector3.UnitZ, 0f, 60f)!.Value.Npc);
    }

    [Fact]
    public void ResidentsArePickedAndDescribed()
    {
        using var g = Arena();
        var origin = Origin(g);
        var stand = g.World.Navigator.NearestStandable(g.Floor + new Int3(0, 1, 3), Strata.Core.Gameplay.Navigation.NavAgent.Humanoid, 3)!.Value;
        var guide = g.World.Town.Summon(g.Content.TownNpcs.Get("guide"), stand);
        var dir = Vector3.Normalize(guide.Center - origin);
        var pick = EntityPicking.Pick(g.World, origin, dir, 0f, 60f);
        Assert.NotNull(pick);
        Assert.Same(guide, pick!.Value.Resident);
        Assert.Null(pick.Value.Npc);
        var (name, life, max) = EntityPicking.Describe(pick.Value);
        Assert.Equal(guide.Title, name);
        Assert.EndsWith(" the Guide", name);
        Assert.Equal((guide.Life, guide.Def.Life), (life, max));
    }

    [Fact]
    public void PickingAndVolumesDoNotAllocate()
    {
        using var g = Arena();
        var origin = Origin(g);
        Spawn(g, "green_slime", origin + new Vector3(0, -0.25f, 4f));
        var worm = Spawn(g, "eater_of_worlds", origin + new Vector3(3f, 6f, 8f));
        var stand = g.World.Navigator.NearestStandable(g.Floor + new Int3(4, 1, 3), Strata.Core.Gameplay.Navigation.NavAgent.Humanoid, 3)!.Value;
        g.World.Town.Summon(g.Content.TownNpcs.Get("guide"), stand);
        float sum = 0;
        EntityPicking.Pick(g.World, origin, Vector3.UnitZ, 0f, 60f); // warm up
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 200; i++)
        {
            var dir = Vector3.Normalize(new Vector3((i % 7 - 3) * 0.1f, (i % 5 - 2) * 0.1f, 1f));
            if (EntityPicking.Pick(g.World, origin, dir, 0f, 60f) is { } p) sum += p.Distance;
            for (int v = 0; v < worm.VolumeCount; v++) sum += worm.VolumeAt(v).Max.Y;
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
        Assert.True(sum > 0);
        Assert.Equal(1 + worm.Segments.Count, worm.VolumeCount);
        Assert.Equal(worm.Volumes().ToList(), Enumerable.Range(0, worm.VolumeCount).Select(worm.VolumeAt).ToList());
    }
}
