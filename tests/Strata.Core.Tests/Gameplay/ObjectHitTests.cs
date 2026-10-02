using Strata.Core.Common;
using Strata.Core.Gameplay.Building;
using Strata.Core.Gameplay.Interaction;
using Strata.Core.Tests.Support;

namespace Strata.Core.Tests.Gameplay;

public class ObjectHitTests
{
    [Fact]
    public void ChoppingATreeRaisesHitsThenBroken()
    {
        using var g = TestGame.Create();
        var tree = g.Content.TileObjects.Get("oak_tree");
        var anchor = g.Floor + new Int3(4, 1, 0);
        Assert.Equal(PlacementResult.Ok, g.World.Editor.PlaceObject(tree, anchor, 0));
        var obj = g.Session().World.TileObjects.At(anchor)!;
        var results = new List<ObjectHitResult>();
        g.World.Editor.ObjectHit += (o, r) => { if (o == obj) results.Add(r); };
        var axe = g.Content.Items.Get("copper_axe");
        while (g.World.Editor.HitObject(obj, axe) == ObjectHitResult.Damaged) { }
        Assert.Equal(ObjectHitResult.Broken, results[^1]);
        Assert.True(results.Count(r => r == ObjectHitResult.Damaged) >= 1);
        Assert.True(tree.Topple);
        Assert.Equal("wood", tree.HitSound);
        Assert.NotNull(tree.Leaves);
    }

    [Fact]
    public void AnObjectTooToughRaisesTooWeak()
    {
        using var g = TestGame.Create();
        var altar = g.Content.TileObjects.All.First(t => t.MinPower > 50 || t.RequiresFlag != null);
        var anchor = g.Floor + new Int3(-5, 1, 0);
        g.World.Editor.PlaceObject(altar, anchor, 0);
        var obj = g.Session().World.TileObjects.At(anchor)!;
        ObjectHitResult? seen = null;
        g.World.Editor.ObjectHit += (_, r) => seen = r;
        var tool = altar.BreakTool switch
        {
            Strata.Core.Content.Defs.BreakTool.Axe => "copper_axe",
            Strata.Core.Content.Defs.BreakTool.Hammer => "copper_hammer",
            _ => "copper_pickaxe",
        };
        Assert.Equal(ObjectHitResult.TooWeak, g.World.Editor.HitObject(obj, g.Content.Items.Get(tool)));
        Assert.Equal(ObjectHitResult.TooWeak, seen);
    }
}

internal static class TestGameSessionExtensions
{
    public static Strata.Core.Session.WorldSession Session(this TestGame g) => g.World.Session;
}
