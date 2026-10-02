using System.Numerics;
using Strata.Core.Gameplay.Entities;
using Strata.Core.Gameplay.Interaction;
using Strata.Core.Gameplay.Items;
using Strata.Core.Tests.Support;

namespace Strata.Core.Tests.Gameplay;

/// <summary>Whips (P3-14): the lash hits along its path with falloff, tags what it strikes, and minions focus tagged creatures.</summary>
public class WhipTests
{
    private static TestGame Arena(string whip = "leather_whip")
    {
        var g = TestGame.Create();
        g.World.Spawner.Enabled = false;
        g.Hold(whip);
        g.Player.ViewDirection = Vector3.UnitZ;
        g.Player.Aim = Vector3.UnitZ;
        g.Player.AimDistance = 10f;
        return g;
    }

    private static Npc Dummy(TestGame g, float ahead, float side = 0f)
    {
        var npc = g.World.Npcs.Spawn(g.Content.Enemies.Get("zombie"), g.Player.Position + new Vector3(side, 0.05f, ahead))!;
        npc.Life = npc.MaxLife = 5000;
        npc.Timer = -100_000; // sit still
        return npc;
    }

    private static void Crack(TestGame g)
    {
        g.Player.ResetUse();
        Assert.Equal(UseOutcome.Swung, g.World.ItemUse.Use(g.Player, null, true));
        g.Ticks(g.Player.UseAnimationMax + 2);
    }

    [Fact]
    public void TheLashHitsAlongItsPathWithFalloffButNotBeyondItsReach()
    {
        using var g = Arena();
        var near = Dummy(g, 2.5f);
        var behind = Dummy(g, 3.6f);
        var far = Dummy(g, 8f, 1f);
        Crack(g);
        int nearLoss = 5000 - near.Life, behindLoss = 5000 - behind.Life;
        Assert.True(nearLoss > 0, "the lash reaches 2.5 m");
        Assert.True(behindLoss > 0, "and the creature behind");
        Assert.True(behindLoss < nearLoss, $"the second takes less ({behindLoss} vs {nearLoss})");
        Assert.Equal(5000, far.Life);
        Assert.Same(g.Player, near.TaggedBy);
        Assert.True(near.Effects.MinionTagDamage > 0, "tagged");
    }

    [Fact]
    public void HoldingDoesNotRepeatTheWhip()
    {
        using var g = Arena();
        g.Player.ResetUse();
        Assert.Equal(UseOutcome.Swung, g.World.ItemUse.Use(g.Player, null, true));
        g.Ticks(g.Player.UseAnimationMax + 2);
        Assert.Equal(UseOutcome.None, g.World.ItemUse.Use(g.Player, null, false));
    }

    [Fact]
    public void FeralClawsLetHoldingRepeatTheWhip()
    {
        using var g = Arena();
        g.Player.State.Inventory.Accessories[0] = new ItemStack(g.Content.Items.Get("feral_claws"), 1);
        g.Player.State.Recompute();
        g.Player.ResetUse();
        Assert.Equal(UseOutcome.Swung, g.World.ItemUse.Use(g.Player, null, true));
        g.Ticks(g.Player.UseAnimationMax + 2);
        Assert.Equal(UseOutcome.Swung, g.World.ItemUse.Use(g.Player, null, false));
    }

    [Fact]
    public void MinionsFocusTheTaggedCreatureAndHitItHarder()
    {
        using var g = Arena();
        var tagged = Dummy(g, 2.5f);
        Crack(g);
        Assert.Same(g.Player, tagged.TaggedBy);
        // A nearer, untagged creature beside the player: the minion still goes for the tagged one.
        var nearer = Dummy(g, 0.5f, 1.5f);
        g.Hold("slime_staff");
        g.Player.AimDistance = 2f;
        g.Player.ResetUse();
        g.World.ItemUse.Use(g.Player, null, true);
        int before = tagged.Life;
        g.Ticks(180);
        Assert.True(tagged.Life < before, "the minion attacked the tagged creature");
        Assert.Equal(5000, nearer.Life);
    }

    [Fact]
    public void SnapthornPoisons()
    {
        using var g = Arena("snapthorn");
        var npc = Dummy(g, 3f);
        Crack(g);
        Assert.True(npc.Buffs.Has("poisoned"));
    }
}
